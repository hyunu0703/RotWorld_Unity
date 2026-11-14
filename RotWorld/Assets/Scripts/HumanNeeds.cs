using System;

namespace RotWorld
{
    /// <summary>
    /// 인간 개인의 게임 경과 시간에 따른 허기와 갈증 보관
    /// </summary>
    public sealed class HumanNeeds
    {
        // 최대 수치와 보급 없는 상태의 고갈 시간
        public const double MaximumValue = 100d;
        private const double SecondsPerDay = 24d * 60d * 60d;
        private const double HungerDuration = 7d * SecondsPerDay;
        private const double ThirstDuration = 3d * SecondsPerDay;

        // 생성 이후 개인별 경과 시간과 0 이상으로 제한한 현재 수치
        private double elapsedGameSeconds;
        public double Hunger => MaximumValue * Math.Max(0d, 1d - elapsedGameSeconds / HungerDuration);
        public double Thirst => MaximumValue * Math.Max(0d, 1d - elapsedGameSeconds / ThirstDuration);

        // 공용 시계의 게임 경과 시간을 개인별 생존 수치에 반영
        public void Advance(double gameSeconds)
        {
            // 정지와 잘못된 시간 입력에 의한 수치 변경 차단
            if (gameSeconds <= 0d || double.IsNaN(gameSeconds) || double.IsInfinity(gameSeconds))
            {
                return;
            }

            // 두 수치 고갈 이후의 불필요한 시간 누적 제한
            elapsedGameSeconds = Math.Min(HungerDuration, elapsedGameSeconds + gameSeconds);
        }
    }
}
