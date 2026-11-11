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

        // 좀비 반대 방향으로 이동 가능한 도주 경로 탐색
        public static void FindFleePath(CityMap map, Vector2Int start, Vector2Int threat, List<Vector2Int> path)
        {
            // 공통 보행 탐색에서 도주 방향과 위협 거리 평가 사용
            FindPath(map, start, null, null, path, threat);
        }


        // 배회와 추적 및 도주의 공통 보행 탐색
        private static void FindPath(CityMap map, Vector2Int start, System.Random random, Vector2Int? target, List<Vector2Int> path, Vector2Int? threat = null)
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

            while (frontier.Count > 0)
            {
                var node = frontier.Min;
                frontier.Remove(node);
                var tile = start + new Vector2Int(node.Index % Width - Radius, node.Index / Width - Radius);

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

                // 반대 방향 진행과 위협과의 거리 증가를 우선하고 긴 우회 경로 감점
                if (threat.HasValue)
                {
                    const float DistanceWeight = .25f;
                    const float CostPenalty = .2f;
                    Vector2 away = (Vector2)(start - threat.Value);
                    Vector2 displacement = (Vector2)(tile - start);
                    float score = Vector2.Dot(displacement, away.normalized) +
                        ((Vector2)(tile - threat.Value)).magnitude * DistanceWeight - node.Cost * CostPenalty;
                    float initialScore = away.magnitude * DistanceWeight;

                    if (node.Index != center && score - initialScore > bestEscapeScore)
                    {
                        bestEscapeScore = score - initialScore;
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

                        // 도주 경로에서 좀비에게 가까워지는 구간 제외
                        if (threat.HasValue && (next - threat.Value).sqrMagnitude < (tile - threat.Value).sqrMagnitude)
                        {
                            continue;
                        }

                        int index = y * Width + x;
                        float cost = node.Cost + (isDiagonal ? DiagonalDistance : 1f) / SpeedMultiplier(map, next);

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
