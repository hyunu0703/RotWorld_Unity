using System.Collections.Generic;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 타일 영역별 변경 번호와 계산에서 읽은 영역의 유효성 추적
    /// </summary>
    internal sealed class GridChangeTracker
    {
        // 고정 지도 영역별 변경 상태
        private const int RegionSize = 8;
        private readonly int width;
        private readonly long[] revisions;
        internal long Version { get; private set; }
        private long globalRevision;

        // 지도 크기에 맞는 변경 배열 준비
        internal GridChangeTracker(int size)
        {
            // 지도 밖 좌표는 가장자리 영역에 보수적으로 포함
            width = (size + RegionSize - 1) / RegionSize;
            revisions = new long[width * width];
        }


        // 단일 좌표를 포함한 영역 변경 기록
        internal void Invalidate(Vector2Int tile)
        {
            // 실제 점유 또는 좌표 변경만 해당 영역에 반영
            revisions[GetRegion(tile)]++;
            Version++;
        }


        // 변경 좌표가 알려지지 않은 외부 요청의 전체 무효화
        internal void InvalidateAll()
        {
            // 기존 전체 변경 통지 계약 유지
            globalRevision++;
            Version++;
        }


        // 경계 좌표를 포함한 영역 인덱스 계산
        private int GetRegion(Vector2Int tile)
        {
            // 지도 내부 정수 나눗셈과 외부 좌표의 가장자리 보정
            int x = Mathf.Clamp(tile.x / RegionSize, 0, width - 1);
            int y = Mathf.Clamp(tile.y / RegionSize, 0, width - 1);
            return y * width + x;
        }


        /// <summary>
        /// 이전 계산이 읽은 영역의 변경 여부 확인과 갱신
        /// </summary>
        internal sealed class Watch
        {
            // 계산 의존 영역과 마지막 확인 시점
            private readonly Dictionary<int, long> observed = new Dictionary<int, long>();
            private GridChangeTracker tracker;
            private int lastRecordedRegion = -1;
            private long version;
            private long globalRevision;

            // 새 계산의 의존 영역 수집 시작
            internal void Reset(GridChangeTracker tracker)
            {
                // 이전 지도와 계산의 기록 제거
                this.tracker = tracker;
                version = tracker.Version;
                globalRevision = tracker.globalRevision;
                observed.Clear();
                lastRecordedRegion = -1;
            }


            // 계산에서 실제로 읽은 타일의 영역 기록
            internal void Record(Vector2Int tile)
            {
                // 동일 영역의 반복 탐색에서 최초 번호만 보관
                int region = tracker.GetRegion(tile);

                if (region == lastRecordedRegion)
                {
                    return;
                }

                lastRecordedRegion = region;

                if (!observed.ContainsKey(region))
                {
                    observed.Add(region, tracker.revisions[region]);
                }
            }


            // 공간 후보 전체를 포함하는 보수적 의존 영역 기록
            internal void RecordArea(Vector2 center, float radius)
            {
                // 후보와 충돌 여유 범위의 모든 영역 포함
                int min = tracker.GetRegion(Vector2Int.FloorToInt(center - Vector2.one * radius));
                int max = tracker.GetRegion(Vector2Int.FloorToInt(center + Vector2.one * radius));

                for (int y = min / tracker.width; y <= max / tracker.width; y++)
                {
                    for (int x = min % tracker.width; x <= max % tracker.width; x++)
                    {
                        int region = y * tracker.width + x;
                        observed[region] = tracker.revisions[region];
                    }
                }
            }


            // 의존 영역이 그대로인 경우 확인 시점 갱신
            internal bool TryRefresh(GridChangeTracker current)
            {
                // 다른 지도와 좌표 미상 변경은 즉시 재계산 요청
                if (tracker != current || globalRevision != current.globalRevision)
                {
                    return false;
                }

                if (version == current.Version)
                {
                    return true;
                }

                foreach (var pair in observed)
                {
                    if (current.revisions[pair.Key] != pair.Value)
                    {
                        return false;
                    }
                }

                version = current.Version;
                return true;
            }
        }
    }
}
