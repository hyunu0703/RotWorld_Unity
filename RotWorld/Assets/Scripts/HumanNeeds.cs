using System;
using System.Collections.Generic;

namespace RotWorld
{
    /// <summary>
    /// 개인 생존 수치와 생성 시점별 정확한 시간 누적 공유
    /// </summary>
    public sealed class HumanNeeds
    {
        // 생성 시 초기 수치와 아이템 회복 상한 및 기존 고갈 기준
        public const double InitialValue = 100d;
        public const double MaximumValue = 150d;
        private const double SecondsPerDay = 24d * 60d * 60d;
        private const double HungerDuration = 7d * SecondsPerDay;
        private const double ThirstDuration = 3d * SecondsPerDay;

        // 개인이 참조하는 시간 상태와 지도 소유 진행 목록
        private Timeline timeline;
        private Schedule schedule;
        public double Hunger => InitialValue * Math.Max(0d, 1d - timeline.Elapsed / HungerDuration);
        public double Thirst => InitialValue * Math.Max(0d, 1d - timeline.ThirstElapsed / ThirstDuration);

        // 독립 생존 수치 생성
        public HumanNeeds()
        {
            // 기존 외부 생성 계약의 독립적인 시간 상태 유지
            timeline = new Timeline(0d);
        }


        // 같은 진행 시점의 인간에게 공유 시간 연결
        private HumanNeeds(Timeline timeline, Schedule schedule)
        {
            // 개인별 표시 객체는 유지하고 동일 누적 연산만 공유
            this.timeline = timeline;
            this.schedule = schedule;
            timeline.AddMember();
        }


        // 개인별 외부 시간 추가 요청 반영
        public void Advance(double gameSeconds)
        {
            // 기존 잘못된 입력 거부 규칙 유지
            if (!IsValid(gameSeconds))
            {
                return;
            }

            // 개인별 변경이 같은 시점의 다른 인간에게 전파되지 않도록 분리
            if (schedule != null)
            {
                timeline = schedule.Split(timeline);
            }

            timeline.Advance(gameSeconds);
        }


        // 아이템 회복량을 개인 생존 수치에 즉시 적용
        public bool UseItem(ItemDefinition item)
        {
            // 아이템 정보 누락과 회복 가능한 수치 확인
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if ((item.HungerRecovery <= 0 || Hunger >= MaximumValue) &&
                (item.ThirstRecovery <= 0 || Thirst >= MaximumValue))
            {
                return false;
            }

            // 같은 시간 상태를 공유하는 다른 인간의 수치 보호
            if (schedule != null)
            {
                timeline = schedule.Split(timeline);
            }

            // 개인 상태에 즉시 회복 적용 후 사용 성공 전달
            timeline.Restore(item.HungerRecovery, item.ThirstRecovery);
            return true;
        }


        // 쓰러짐 또는 제거 시 해당 개인의 생존 수치 고정
        internal void Freeze()
        {
            // 이미 분리된 수치의 반복 해제 생략
            if (schedule == null)
            {
                return;
            }

            timeline.RemoveMember();
            timeline = new Timeline(timeline.Elapsed, thirstElapsed: timeline.ThirstElapsed);
            schedule = null;
        }


        // 진행 가능한 게임 시간 여부 확인
        private static bool IsValid(double seconds)
        {
            // 원래 개인별 진행 메서드와 동일한 입력 허용 범위
            return seconds > 0d && !double.IsNaN(seconds) && !double.IsInfinity(seconds);
        }


        /// <summary>
        /// 같은 생성 시점의 개인들이 공유하는 누적 시간 상태
        /// </summary>
        internal sealed class Timeline
        {
            // 개인 수와 기존 덧셈 순서로 누적한 게임 시간
            internal int Members { get; private set; }
            internal double Elapsed { get; private set; }
            internal double ThirstElapsed { get; private set; }

            // 독립 또는 공유 상태의 시작 시간 구성
            internal Timeline(double elapsed, int members = 1, double? thirstElapsed = null)
            {
                // 분리 시 재계산 없이 기존 비트 수준 누적값 복사
                Elapsed = elapsed;
                ThirstElapsed = thirstElapsed ?? Math.Min(ThirstDuration, elapsed);
                Members = members;
            }


            // 공유 상태에 개인 참조 추가
            internal void AddMember()
            {
                // 생성 시점 묶음의 사용 개체 수 증가
                Members++;
            }


            // 공유 상태에서 개인 참조 반환
            internal void RemoveMember()
            {
                // 분리와 쓰러짐 및 제거의 사용 개체 수 감소
                Members--;
            }


            // 검증된 프레임 시간의 기존 누적 연산 적용
            internal void Advance(double seconds)
            {
                // 개인별 반복과 동일한 덧셈 및 고갈 이후 상한 적용
                Elapsed = Math.Min(HungerDuration, Elapsed + seconds);
                ThirstElapsed = Math.Min(ThirstDuration, ThirstElapsed + seconds);
            }


            // 아이템 회복량을 고갈 시간으로 환산해 차감
            internal void Restore(int hungerRecovery, int thirstRecovery)
            {
                // 초기값 초과분을 음수 시간으로 보관하고 회복 상한에서 제한
                double minimumRatio = 1d - MaximumValue / InitialValue;
                Elapsed = Math.Max(minimumRatio * HungerDuration,
                    Elapsed - Math.Max(0, hungerRecovery) / InitialValue * HungerDuration);
                ThirstElapsed = Math.Max(minimumRatio * ThirstDuration,
                    ThirstElapsed - Math.Max(0, thirstRecovery) / InitialValue * ThirstDuration);
            }
        }


        /// <summary>
        /// 지도별 생성 시점 묶음의 시간 진행과 수명 정리
        /// </summary>
        internal sealed class Schedule
        {
            // 프레임 진행을 공유하는 상태와 다음 진행 전 생성 묶음
            private readonly List<Timeline> timelines = new List<Timeline>();
            private Timeline pending;

            // 다음 시간 진행을 함께 시작할 개인 수치 생성
            internal HumanNeeds Create()
            {
                // 마지막 진행 이후 생성된 인간은 같은 0초 상태로 연결
                if (pending == null)
                {
                    pending = new Timeline(0d, 0);
                    timelines.Add(pending);
                }

                return new HumanNeeds(pending, this);
            }


            // 개인별 외부 변경을 위한 공유 상태 분리
            internal Timeline Split(Timeline previous)
            {
                // 이전 묶음과 동일한 누적값으로 다음 프레임 진행 유지
                previous.RemoveMember();
                var timeline = new Timeline(previous.Elapsed, thirstElapsed: previous.ThirstElapsed);
                timelines.Add(timeline);
                return timeline;
            }


            // 프레임 게임 시간을 생성 시점 묶음별로 한 번씩 누적
            internal void Advance(double seconds)
            {
                // 정지와 잘못된 입력의 묶음 변경 차단
                if (!IsValid(seconds))
                {
                    return;
                }

                pending = null;

                for (int i = timelines.Count - 1; i >= 0; i--)
                {
                    Timeline timeline = timelines[i];

                    if (timeline.Members > 0)
                    {
                        timeline.Advance(seconds);
                    }

                    // 대상이 없거나 모든 수치가 고갈된 묶음의 반복 연산 제거
                    if (timeline.Members == 0 ||
                        (timeline.Elapsed >= HungerDuration && timeline.ThirstElapsed >= ThirstDuration))
                    {
                        timelines.RemoveAt(i);
                    }
                }
            }
        }
    }
}
