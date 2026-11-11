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

            while (frontier.Count > 0)
            {
                var node = frontier.Min;
                frontier.Remove(node);
                var tile = start + new Vector2Int(node.Index % Width - Radius, node.Index / Width - Radius);

                // 충분히 떨어진 도달 가능 타일 중 균등 무작위 목적지 선택
                if ((tile - start).sqrMagnitude >= Radius)
                {
                    candidates++;

                    if (random.Next(candidates) == 0)
                    {
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
                            !CanOccupy(map, next) || (isDiagonal &&
                            (!CanOccupy(map, tile + new Vector2Int(dx, 0)) || !CanOccupy(map, tile + new Vector2Int(0, dy)))))
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
            if (destination < 0)
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
