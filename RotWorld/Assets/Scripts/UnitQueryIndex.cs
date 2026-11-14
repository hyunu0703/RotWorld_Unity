using System.Collections.Generic;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 실시간 행동 좌표의 주변 검색과 기존 사전 순서 보존
    /// </summary>
    internal sealed class UnitQueryIndex : IComparer<int>
    {
        // 기존 개체 사전과 실시간 좌표 격자 및 순회 순위
        private readonly Dictionary<int, UnitWander> units;
        private readonly UnitSpatialIndex spatialIndex = new UnitSpatialIndex();
        private readonly Dictionary<int, int> ranks = new Dictionary<int, int>();
        private readonly HashSet<int> orderingCandidates = new HashSet<int>();
        private bool isOrderDirty = true;

        // 지도 소유 개체 사전 연결
        internal UnitQueryIndex(Dictionary<int, UnitWander> units)
        {
            // 키 재사용 이후에도 실제 사전 순회를 기준으로 순위 생성
            this.units = units;
        }


        // 등록과 제거에 따른 순회 순위 무효화
        internal void InvalidateOrder()
        {
            // 다음 정렬 요청 전까지 순위 재구성 지연
            isOrderDirty = true;
        }


        // 소단계 이동 직후의 실제 행동 좌표 반영
        internal void SetPosition(int index, Vector2 position)
        {
            // 프레임 끝 공유 좌표와 별도로 최신 행동 위치 등록
            spatialIndex.SetPosition(index, position);
        }


        // 쓰러지거나 제거된 개체의 주변 검색 제외
        internal void Remove(int index)
        {
            // 순회 순위와 독립적인 활동 좌표 제거
            spatialIndex.Remove(index);
        }


        // 기존 사전 순서로 주변 행동 후보 반환
        internal void Collect(Vector2 position, float radius, List<int> results, bool preserveOrder = true)
        {
            // 거리와 생존 여부의 최종 판정은 기존 호출자 규칙 유지
            spatialIndex.Collect(position, radius, results);

            if (!preserveOrder || results.Count < 2)
            {
                return;
            }

            SortInOriginalOrder(results);
        }


        // 사전 변경 뒤 비교 순위 재구성
        private void RefreshOrder()
        {
            // 등록과 제거가 없으면 기존 순위 재사용
            if (isOrderDirty)
            {
                ranks.Clear();
                int rank = 0;

                foreach (int index in units.Keys)
                {
                    ranks.Add(index, rank++);
                }

                isOrderDirty = false;
            }
        }


        // 관련 개체 통지의 기존 사전 순서 복원
        internal void SortInOriginalOrder(List<int> results)
        {
            // 통지 대상이 여럿인 경우에만 기존 순위 복원
            if (results.Count < 2)
            {
                return;
            }

            // 관련 개체가 많은 통지는 전체 순위 정렬보다 선형 필터 순회 사용
            if (results.Count > units.Count / 4)
            {
                orderingCandidates.Clear();

                foreach (int index in results)
                {
                    orderingCandidates.Add(index);
                }

                results.Clear();

                foreach (int index in units.Keys)
                {
                    if (orderingCandidates.Contains(index))
                    {
                        results.Add(index);
                    }
                }

                return;
            }

            RefreshOrder();
            results.Sort(this);
        }


        // 기존 개체 사전의 실제 순회 순서 비교
        public int Compare(int first, int second)
        {
            // 동일 거리 후보의 기존 선택 순서 유지
            RefreshOrder();
            return ranks[first].CompareTo(ranks[second]);
        }
    }
}
