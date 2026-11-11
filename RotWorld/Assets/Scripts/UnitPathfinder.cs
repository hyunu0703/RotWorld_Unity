using System.Collections.Generic;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 주변 보행 영역의 지형 비용 기반 8방향 경로 탐색
    /// </summary>
    public static class UnitPathfinder
    {
        // 배회 탐색 범위와 대각선 이동 거리
        private const int Radius = 8;
        private const int Width = Radius * 2 + 1;
        private const float DiagonalDistance = 1.41421356f;

        // 보행 가능 타일 여부 조회
        public static bool CanOccupy(CityMap map, Vector2Int tile)
        {
            // 지도 경계 검사 후 통행 가능한 지형만 허용
            if (map == null || tile.x < 0 || tile.y < 0 || tile.x >= map.Size || tile.y >= map.Size)
            {
                return false;
            }

            byte ground = map.Tiles[tile.y * map.Size + tile.x];
            return ground == CityGenerator.Grass || ground == CityGenerator.Road ||
                ground == CityGenerator.Pavement || ground == CityGenerator.Path ||
                ground == CityGenerator.Sand || ground == CityGenerator.Bridge;
        }


        // 표시 영역 전체의 건물 및 지도 경계 침범 여부 조회
        internal static bool HasBuildingClearance(CityMap map, Rect bounds)
        {
            // 건물 표시와 동일한 정수 타일 경계 기준으로 몸체 영역 검사
            if (map == null || bounds.xMin < 0f || bounds.yMin < 0f || bounds.xMax > map.Size || bounds.yMax > map.Size)
            {
                return false;
            }

            int minX = Mathf.FloorToInt(bounds.xMin);
            int minY = Mathf.FloorToInt(bounds.yMin);
            int maxX = Mathf.CeilToInt(bounds.xMax) - 1;
            int maxY = Mathf.CeilToInt(bounds.yMax) - 1;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (map.Tiles[y * map.Size + x] == CityGenerator.Building)
                    {
                        return false;
                    }
                }
            }

            return true;
        }


        // 타일 지형에 따른 이동 속도 배율 조회
        public static float SpeedMultiplier(CityMap map, Vector2Int tile)
        {
            // 모래와 초지의 감속 및 도로의 이동 이점 반영
            const float SandSpeed = .65f;
            const float GrassSpeed = .85f;
            const float RoadSpeed = 1.15f;
            byte ground = map.Tiles[tile.y * map.Size + tile.x];
            return ground == CityGenerator.Sand ? SandSpeed : ground == CityGenerator.Grass ? GrassSpeed : RoadSpeed;
        }


        // 도달 가능한 주변 목적지 선택과 최소 비용 경로 반환
        public static void FindWanderPath(CityMap map, Vector2Int start, System.Random random, List<Vector2Int> path)
        {
            // 기존 주변 탐색에서 무작위 목적지 선택
            FindPath(map, start, random, null, path);
        }


        // 실제 구간 이동 시간을 비용으로 사용하는 추적 전용 A스타 경로 탐색
        public static void FindTargetPath(CityMap map, Vector2Int start, Vector2Int target, List<Vector2Int> path, bool approachOccupiedTarget = false)
        {
            // 지도 전체에서 탐색해 배회 반경으로 인한 우회 경로 누락 방지
            path.Clear();

            if (!CanOccupy(map, start) || !CanOccupy(map, target) || start == target)
            {
                return;
            }

            var costs = new Dictionary<Vector2Int, float> { [start] = 0f };
            var parents = new Dictionary<Vector2Int, Vector2Int>();
            var frontier = new SortedSet<(float Estimate, float Remaining, float Cost, int Index)>();
            float initialEstimate = approachOccupiedTarget ? 0f : EstimateTime(start, target);
            frontier.Add((initialEstimate, initialEstimate, 0f, start.y * map.Size + start.x));

            while (frontier.Count > 0)
            {
                var node = frontier.Min;
                frontier.Remove(node);
                var tile = new Vector2Int(node.Index % map.Size, node.Index / map.Size);

                if (node.Cost > costs[tile])
                {
                    continue;
                }

                // 추적 중에는 인간의 점유 타일 진입 대신 인접한 빈 위치까지 접근
                bool hasApproached = approachOccupiedTarget && (tile - target).sqrMagnitude <= 2 &&
                    UnitSight.HasLineOfSight(map, tile, target);

                if (tile == target || hasApproached)
                {
                    // 최저 시간 경로를 시작점에서 인간 위치 순서로 반환
                    for (var step = tile; step != start; step = parents[step])
                    {
                        path.Add(step);
                    }

                    path.Reverse();
                    return;
                }

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var next = tile + new Vector2Int(dx, dy);
                        bool isDiagonal = dx != 0 && dy != 0;

                        // 실제 이동 예약과 동일한 점유 및 대각선 모서리 검사
                        if ((dx == 0 && dy == 0) || !CanOccupy(map, next) ||
                            ((approachOccupiedTarget || next != target) && map.OccupiedTiles.Contains(next)))
                        {
                            continue;
                        }

                        if (isDiagonal && (!CanOccupy(map, tile + new Vector2Int(dx, 0)) ||
                            !CanOccupy(map, tile + new Vector2Int(0, dy)) ||
                            map.OccupiedTiles.Contains(tile + new Vector2Int(dx, 0)) ||
                            map.OccupiedTiles.Contains(tile + new Vector2Int(0, dy))))
                        {
                            continue;
                        }

                        // 실제 이동과 동일하게 출발 타일 배율과 대각선 거리를 시간 비용에 반영
                        float cost = node.Cost + (isDiagonal ? DiagonalDistance : 1f) / SpeedMultiplier(map, tile);

                        if (costs.TryGetValue(next, out float previous) && cost >= previous)
                        {
                            continue;
                        }

                        costs[next] = cost;
                        parents[next] = tile;
                        // 최단 시간이 같으면 목표에 가까운 후보 우선으로 대각선 진행 유지
                        float remaining = approachOccupiedTarget ? 0f : EstimateTime(next, target);
                        frontier.Add((cost + remaining, remaining, cost, next.y * map.Size + next.x));
                    }
                }
            }
        }


        // 최고 지형 속도를 가정한 최소 잔여 이동 시간 추정
        private static float EstimateTime(Vector2Int from, Vector2Int to)
        {
            // 과대 추정 없는 8방향 거리로 최저 시간 경로 보장
            const float MaximumTerrainSpeed = 1.15f;
            int dx = Mathf.Abs(to.x - from.x);
            int dy = Mathf.Abs(to.y - from.y);
            return (Mathf.Max(dx, dy) + (DiagonalDistance - 1f) * Mathf.Min(dx, dy)) / MaximumTerrainSpeed;
        }

        // 주변 좀비 사이의 빈 방향을 우선하는 도주 경로 탐색
        public static void FindFleePath(CityMap map, Vector2Int start, Vector2Int threat, IReadOnlyList<Vector2> threats, List<Vector2Int> path)
        {
            // 주변 위협 전체를 공통 보행 탐색의 방향과 구간 위험도 평가에 전달
            FindPath(map, start, null, null, path, threat, threats);
        }


        // 주변 좀비 방향과의 최소 각도 간격이 가장 큰 도주 방향 선택
        private static Vector2 FindEscapeDirection(Vector2 start, Vector2 threat, IReadOnlyList<Vector2> threats)
        {
            // 한 좀비의 반대 방향을 기준으로 원형 후보 평가 및 동점 방향 유지
            const int DirectionCount = 32;
            Vector2 away = (start - threat).normalized;
            float baseAngle = Mathf.Atan2(away.y, away.x);
            float bestAlignment = float.PositiveInfinity;
            Vector2 bestDirection = away;

            for (int i = 0; i < DirectionCount; i++)
            {
                float angle = baseAngle + i * (Mathf.PI * 2f / DirectionCount);
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float alignment = Vector2.Dot(direction, (threat - start).normalized);

                for (int j = 0; j < threats.Count; j++)
                {
                    alignment = Mathf.Max(alignment, Vector2.Dot(direction, (threats[j] - start).normalized));
                }

                if (alignment < bestAlignment)
                {
                    bestAlignment = alignment;
                    bestDirection = direction;
                }
            }

            return bestDirection;
        }


        // 이동 선분과 전체 위협 사이의 최소 거리 조회
        private static float NearestThreatDistance(Vector2 from, Vector2 to, Vector2 threat, IReadOnlyList<Vector2> threats)
        {
            // 선분 투영으로 대각선 이동 중 좀비 근접 구간까지 평가
            Vector2 delta = to - from;
            float lengthSquared = delta.sqrMagnitude;
            float nearest = float.PositiveInfinity;

            for (int i = -1; i < threats.Count; i++)
            {
                Vector2 target = i < 0 ? threat : threats[i];
                float progress = lengthSquared > 0f ? Mathf.Clamp01(Vector2.Dot(target - from, delta) / lengthSquared) : 0f;
                nearest = Mathf.Min(nearest, (target - (from + delta * progress)).magnitude);
            }

            return nearest;
        }


        // 배회와 추적 및 도주의 공통 보행 탐색
        private static void FindPath(CityMap map, Vector2Int start, System.Random random, Vector2Int? target, List<Vector2Int> path,
            Vector2Int? threat = null, IReadOnlyList<Vector2> threats = null)
        {
            // 제한된 지역 배열과 비용 우선 탐색 큐 준비
            path.Clear();
            var costs = new float[Width * Width];
            var parents = new int[costs.Length];
            var frontier = new SortedSet<(float Cost, int Index)>();
            int center = Radius * Width + Radius;

            for (int i = 0; i < costs.Length; i++)
            {
                costs[i] = float.PositiveInfinity;
                parents[i] = -1;
            }

            costs[center] = 0f;
            frontier.Add((0f, center));
            int destination = -1;
            int candidates = 0;
            float bestEscapeScore = 0f;
            Vector2 escapeDirection = threat.HasValue ? FindEscapeDirection(start, threat.Value, threats) : Vector2.zero;
            float initialThreatDistance = threat.HasValue ? NearestThreatDistance(start, start, threat.Value, threats) : 0f;

            while (frontier.Count > 0)
            {
                var node = frontier.Min;
                frontier.Remove(node);
                var tile = start + new Vector2Int(node.Index % Width - Radius, node.Index / Width - Radius);

                // 안전 거리에 도달하는 최저 비용 타일에서 도주 구간 종료
                if (threat.HasValue && NearestThreatDistance(tile, tile, threat.Value, threats) >= UnitSight.FleeReleaseRadius)
                {
                    destination = node.Index;
                    break;
                }

                // 추적 목적지 도달 시 탐색 종료 또는 배회 목적지 선택
                if (target.HasValue && tile == target.Value)
                {
                    destination = node.Index;
                    break;
                }

                // 충분히 떨어진 도달 가능 타일 중 균등 무작위 목적지 선택
                if (!target.HasValue && !threat.HasValue && (tile - start).sqrMagnitude >= Radius)
                {
                    candidates++;

                    if (random.Next(candidates) == 0)
                    {
                        destination = node.Index;
                    }
                }

                // 좀비가 없는 방향 진행과 전체 위협의 최소 거리 증가 우선
                if (threat.HasValue)
                {
                    const float DistanceWeight = .25f;
                    const float CostPenalty = .2f;
                    Vector2 displacement = (Vector2)(tile - start);
                    float score = Vector2.Dot(displacement, escapeDirection) +
                        (NearestThreatDistance(tile, tile, threat.Value, threats) - initialThreatDistance) * DistanceWeight - node.Cost * CostPenalty;

                    if (node.Index != center && score > bestEscapeScore)
                    {
                        bestEscapeScore = score;
                        destination = node.Index;
                    }
                }

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = node.Index % Width + dx;
                        int y = node.Index / Width + dy;
                        var next = tile + new Vector2Int(dx, dy);
                        bool isDiagonal = dx != 0 && dy != 0;

                        // 장애물 모서리 사이의 대각선 관통과 탐색 범위 이탈 차단
                        if ((dx == 0 && dy == 0) || x < 0 || y < 0 || x >= Width || y >= Width ||
                            !CanOccupy(map, next) || (map.OccupiedTiles.Contains(next) && (!target.HasValue || next != target.Value)) || (isDiagonal &&
                            (!CanOccupy(map, tile + new Vector2Int(dx, 0)) || !CanOccupy(map, tile + new Vector2Int(0, dy)) ||
                            map.OccupiedTiles.Contains(tile + new Vector2Int(dx, 0)) ||
                            map.OccupiedTiles.Contains(tile + new Vector2Int(0, dy)))))
                        {
                            continue;
                        }

                        // 빈 방향 우회를 허용하되 좀비 몸체로 접근하는 구간 차단 및 근접 비용 가산
                        float dangerCost = 0f;

                        if (threat.HasValue)
                        {
                            const float MinimumThreatClearance = 1f;
                            const float DangerWeight = 4f;
                            float clearance = NearestThreatDistance(tile, next, threat.Value, threats);

                            if (clearance < MinimumThreatClearance &&
                                clearance < NearestThreatDistance(tile, tile, threat.Value, threats))
                            {
                                continue;
                            }

                            dangerCost = DangerWeight / (1f + clearance * clearance);
                        }

                        int index = y * Width + x;
                        float cost = node.Cost + (isDiagonal ? DiagonalDistance : 1f) * (1f / SpeedMultiplier(map, next) + dangerCost);

                        if (cost >= costs[index])
                        {
                            continue;
                        }

                        frontier.Remove((costs[index], index));
                        costs[index] = cost;
                        parents[index] = node.Index;
                        frontier.Add((cost, index));
                    }
                }
            }

            // 좁은 고립 영역에서는 가까운 도달 가능 타일로 대체
            if (destination < 0 && !target.HasValue && !threat.HasValue)
            {
                for (int i = 0; i < parents.Length; i++)
                {
                    if (parents[i] >= 0)
                    {
                        destination = i;
                        break;
                    }
                }
            }

            // 부모 연결을 역추적한 뒤 시작점에서 목적지 순서로 변환
            for (int i = destination; i >= 0 && i != center; i = parents[i])
            {
                path.Add(start + new Vector2Int(i % Width - Radius, i / Width - Radius));
            }

            path.Reverse();
        }
    }
}
