using System;

namespace RotWorld
{
    /// <summary>
    /// 배속 적용 경과 시간을 게임 시각과 24시간 단위 일차로 변환
    /// </summary>
    public sealed class GameClock
    {
        // 현실 1초당 게임 1분과 하루 경계 계산
        private const double GameSecondsPerSecond = 60d;
        private const int SecondsPerMinute = 60;
        private const int SecondsPerHour = 60 * SecondsPerMinute;
        private const int SecondsPerDay = 24 * SecondsPerHour;
        private double elapsedGameSeconds;

        public long ElapsedSeconds => (long)Math.Floor(elapsedGameSeconds);
        public long CurrentDay => ElapsedSeconds / SecondsPerDay + 1;
        public int Hour => (int)(ElapsedSeconds % SecondsPerDay / SecondsPerHour);
        public int Minute => (int)(ElapsedSeconds % SecondsPerHour / SecondsPerMinute);
        public int Second => (int)(ElapsedSeconds % SecondsPerMinute);

        // 배속 적용 프레임 시간에서 게임 경과 시간 누적
        public void Advance(float deltaTime)
        {
            // 정지 또는 유효하지 않은 입력의 시계 상태 변경 차단
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                return;
            }

            // Unity 배속 적용 시간에 게임 시간 비율만 추가 적용
            elapsedGameSeconds += deltaTime * GameSecondsPerSecond;
        }


        // 새 지도 시작 시 첫날 자정으로 초기화
        public void Reset()
        {
            // 이전 지도와 독립된 게임 경과 시간 복원
            elapsedGameSeconds = 0d;
        }
    }
}
