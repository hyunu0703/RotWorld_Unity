using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 좀비의 거리 제한과 건물 차폐를 고려한 인간 감지
    /// </summary>
    public static class UnitSight
    {
        // 원형 감지 반경
        public const float DetectionRadius = 5f;

        // 감지 반경 안에서 건물에 가려지지 않은 가장 가까운 인간 조회
        public static int FindVisibleHuman(CityMap map, Vector2 observer)
        {
            // 기존 인구 목록의 최신 타일 위치에서 거리와 시야 검사
            int nearest = -1;
            float bestDistance = DetectionRadius * DetectionRadius;

            for (int i = 0; i < map.Humans.Count; i++)
            {
                Vector2 target = map.Humans[i];
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
