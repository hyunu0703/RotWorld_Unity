using System.Collections.Generic;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 실제 유닛 좌표의 격자별 후보 검색과 이동 및 제거 반영
    /// </summary>
    internal sealed class UnitSpatialIndex
    {
        // 검색 격자와 개체별 현재 격자 보관
        private const float CellSize = 2f;
        private readonly Dictionary<Vector2Int, HashSet<int>> cells = new Dictionary<Vector2Int, HashSet<int>>();
        private readonly Dictionary<int, Vector2Int> locations = new Dictionary<int, Vector2Int>();
        private readonly Stack<HashSet<int>> availableCells = new Stack<HashSet<int>>();

        // 실제 위치 변경에 따른 검색 격자 갱신
        internal void SetPosition(int index, Vector2 position)
        {
            // 같은 격자 안의 이동은 기존 후보 소속 유지
            Vector2Int cell = GetCell(position);

            if (locations.TryGetValue(index, out Vector2Int previous) && previous == cell)
            {
                return;
            }

            Remove(index);

            // 빈 격자 목록 재사용 후 최신 위치 등록
            if (!cells.TryGetValue(cell, out HashSet<int> members))
            {
                members = availableCells.Count > 0 ? availableCells.Pop() : new HashSet<int>();
                cells.Add(cell, members);
            }

            members.Add(index);
            locations[index] = cell;
        }


        // 쓰러짐과 변환 및 파괴 시 검색 후보 제거
        internal void Remove(int index)
        {
            // 등록되지 않은 개체의 반복 제거 생략
            if (!locations.TryGetValue(index, out Vector2Int cell))
            {
                return;
            }

            locations.Remove(index);
            HashSet<int> members = cells[cell];
            members.Remove(index);

            if (members.Count == 0)
            {
                cells.Remove(cell);
                availableCells.Push(members);
            }
        }


        // 요청 범위와 겹치는 격자의 후보를 재사용 목록에 전달
        internal void Collect(Vector2 position, float radius, List<int> results)
        {
            // 정확한 원형 거리 판정은 호출자의 기존 규칙에 위임
            results.Clear();
            Vector2Int min = GetCell(position - Vector2.one * radius);
            Vector2Int max = GetCell(position + Vector2.one * radius);

            for (int y = min.y; y <= max.y; y++)
            {
                for (int x = min.x; x <= max.x; x++)
                {
                    if (cells.TryGetValue(new Vector2Int(x, y), out HashSet<int> members))
                    {
                        foreach (int index in members)
                        {
                            results.Add(index);
                        }
                    }
                }
            }
        }


        // 음수 좌표를 포함한 실제 좌표의 격자 변환
        private static Vector2Int GetCell(Vector2 position)
        {
            // 경계 양쪽 개체의 검색 누락을 막는 내림 변환
            return new Vector2Int(Mathf.FloorToInt(position.x / CellSize), Mathf.FloorToInt(position.y / CellSize));
        }
    }
}
