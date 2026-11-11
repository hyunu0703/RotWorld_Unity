using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 거리 제한과 건물 차폐를 고려한 인간 및 좀비 감지
    /// </summary>
    public static class UnitSight
    {
        // 최초 감지와 유형별 반응 종료 거리
        public const float DetectionRadius = 5f;
        public const float FleeReleaseRadius = 15f;
        private const float ChaseReleaseRadius = 8f;

        // 감지 반경 안에서 건물에 가려지지 않은 가장 가까운 인간 조회
        public static int FindVisibleHuman(CityMap map, Vector2 observer)
        {
            // 인간 목록에 공통 감지 조건 적용
            return FindVisibleUnit(map, observer, map.Humans);
        }


        // 기존 인간 대상의 추적 유지 조건 검사 및 신규 대상 감지
        public static int FindChaseHuman(CityMap map, Vector2 observer, int currentTarget)
        {
            // 기존 대상은 8타일과 건물 차폐 조건을 만족하는 동안 유지
            if (currentTarget >= 0 && currentTarget < map.Humans.Count && !map.InactiveHumans.Contains(currentTarget))
            {
                Vector2 target = map.HumanPositions.TryGetValue(currentTarget, out Vector2 livePosition) ? livePosition : (Vector2)map.Humans[currentTarget];

                if ((target - observer).sqrMagnitude <= ChaseReleaseRadius * ChaseReleaseRadius &&
                    HasLineOfSight(map, observer, target))
                {
                    return currentTarget;
                }
            }

            // 추적 상실 후 새 인간은 최초 감지 반경 5타일에서만 탐색
            return FindVisibleHuman(map, observer);
        }


        // 감지 반경 안에서 건물에 가려지지 않은 가장 가까운 좀비 조회
        public static int FindVisibleZombie(CityMap map, Vector2 observer)
        {
            // 좀비 목록에 인간 감지와 동일한 거리 및 차폐 조건 적용
            return FindVisibleUnit(map, observer, map.Zombies);
        }


        // 새 좀비 감지와 안전 거리 확보 전 기존 도주 대상 유지
        public static int FindFleeZombie(CityMap map, Vector2 observer, int currentTarget)
        {
            // 가까운 시야 내 좀비를 우선해 새로운 위협에 반응
            int visibleTarget = FindVisibleZombie(map, observer);

            if (visibleTarget >= 0)
            {
                return visibleTarget;
            }

            // 감지 범위나 시야를 벗어나도 현재 좀비와 15타일 미만이면 도주 유지
            if (currentTarget >= 0 && currentTarget < map.Zombies.Count)
            {
                Vector2 target = map.ZombiePositions.TryGetValue(currentTarget, out Vector2 livePosition) ? livePosition : (Vector2)map.Zombies[currentTarget];

                if ((target - observer).sqrMagnitude < FleeReleaseRadius * FleeReleaseRadius)
                {
                    return currentTarget;
                }
            }

            return -1;
        }


        // 지정 인구 목록에서 가장 가까운 시야 내 대상 조회
        private static int FindVisibleUnit(CityMap map, Vector2 observer, System.Collections.Generic.List<Vector2Int> units)
        {
            // 기존 인구 목록의 최신 타일 위치에서 거리와 시야 검사
            int nearest = -1;
            float bestDistance = DetectionRadius * DetectionRadius;

            for (int i = 0; i < units.Count; i++)
            {

                if (units == map.Humans && map.InactiveHumans.Contains(i))
                {
                    continue;
                }

                var livePositions = units == map.Humans ? map.HumanPositions : map.ZombiePositions;
                Vector2 target = livePositions.TryGetValue(i, out Vector2 livePosition) ? livePosition : (Vector2)units[i];
                float distance = (target - observer).sqrMagnitude;

                if (distance > bestDistance || !HasLineOfSight(map, observer, target))
                {
                    continue;
                }

                nearest = i;
                bestDistance = distance;
            }

            return nearest;
        }


        // 두 타일 중심을 잇는 선분과 건물 영역의 교차 여부 조회
        public static bool HasLineOfSight(CityMap map, Vector2 from, Vector2 to)
        {
            // 표시 좌표의 타일 원점을 논리 타일 중심으로 변환
            Vector2 start = from + Vector2.one * .5f;
            Vector2 end = to + Vector2.one * .5f;
            Vector2 delta = end - start;
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(start.x, end.x)));
            int maxX = Mathf.Min(map.Size - 1, Mathf.FloorToInt(Mathf.Max(start.x, end.x)));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(start.y, end.y)));
            int maxY = Mathf.Min(map.Size - 1, Mathf.FloorToInt(Mathf.Max(start.y, end.y)));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (map.Tiles[y * map.Size + x] != CityGenerator.Building)
                    {
                        continue;
                    }

                    // 축별 선분 진입과 이탈 구간의 교집합으로 얇은 차폐와 모서리 검사
                    float enter = 0f;
                    float exit = 1f;

                    if (IntersectsAxis(start.x, delta.x, x, ref enter, ref exit) &&
                        IntersectsAxis(start.y, delta.y, y, ref enter, ref exit))
                    {
                        return false;
                    }
                }
            }

            return true;
        }


        // 단일 축에서 선분과 타일 구간의 교차 범위 계산
        private static bool IntersectsAxis(float start, float delta, int tile, ref float enter, ref float exit)
        {
            // 축에 평행한 선분은 해당 타일 폭 안에 있는 경우만 허용
            if (delta == 0f)
            {
                return start >= tile && start <= tile + 1;
            }

            float first = (tile - start) / delta;
            float last = (tile + 1 - start) / delta;
            enter = Mathf.Max(enter, Mathf.Min(first, last));
            exit = Mathf.Min(exit, Mathf.Max(first, last));
            return enter <= exit;
        }
    }
}
