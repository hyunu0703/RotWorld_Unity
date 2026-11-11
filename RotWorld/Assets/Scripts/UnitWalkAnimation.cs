using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 원본 픽셀 기반 걷기 프레임과 이동 방향에 따른 좌우 반전 표시
    /// </summary>
    public sealed class UnitWalkAnimation : MonoBehaviour
    {
        // 이동 거리 기준 보폭과 네 단계 걷기 프레임 순서
        private const float StrideLength = .8f;
        private const float MirrorOffset = .875f;
        private static readonly Color HitColor = new Color32(160, 0, 0, 255);
        private static readonly int[] FrameSequence = { 0, 1, 0, 2 };

        // 공용 프레임과 개체 렌더러 및 누적 이동 거리
        private SpriteRenderer visual;
        private Sprite[] frames;
        private float distance;

        // 지도 표시에서 소유하는 공용 스프라이트와 개체 렌더러 연결
        public void Initialize(SpriteRenderer visual, Sprite[] frames)
        {
            // 필수 표시 자원 검증 후 정지 프레임 적용
            if (visual == null || frames == null || frames.Length < 3)
            {
                Debug.LogError("유닛 걷기 표시의 렌더러 또는 프레임 누락", this);
                enabled = false;
                return;
            }

            this.visual = visual;
            this.frames = frames;
            Stop();
        }


        // 실제 이동 거리로 다리 프레임과 좌우 방향 갱신
        public void Walk(Vector2 direction, float travelled)
        {
            // 수직 이동에서는 이전 좌우 방향 유지
            if (direction.x != 0f)
            {
                visual.flipX = direction.x < 0f;
                visual.transform.localPosition = visual.flipX ? Vector3.right * MirrorOffset : Vector3.zero;
            }

            // 스프라이트 교체만 사용해 원본 머리와 몸통의 픽셀 크기 유지
            distance = Mathf.Repeat(distance + travelled, StrideLength);
            int phase = Mathf.FloorToInt(distance / StrideLength * FrameSequence.Length);
            visual.sprite = frames[FrameSequence[phase]];
        }


        // 피격 중 빨간색과 원래 스프라이트 색상 전환
        public void SetHitColor(bool hit)
        {
            // 공용 텍스처 대신 개체 렌더러 색상만 변경
            visual.color = hit ? HitColor : Color.white;
        }


        // 논리 위치를 바꾸지 않는 공격 준비와 타격 오프셋 표시
        public void SetAttackOffset(Vector2 offset)
        {
            // 좌우 반전 기준 위치에 공격 모션만 합성
            visual.transform.localPosition = (visual.flipX ? Vector3.right * MirrorOffset : Vector3.zero) + (Vector3)offset;
        }


        // 휴식 시 다리 정지 포즈로 복원
        public void Stop()
        {
            // 프레임 진행 초기화와 원본 아이콘 복원
            distance = 0f;
            visual.sprite = frames[0];
        }
    }
}
