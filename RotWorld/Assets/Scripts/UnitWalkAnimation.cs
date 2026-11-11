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
        private int humanSortingOrder;
        private bool isZombieVisual;
        private const int ZombieSortingOffset = 1;
        private Sprite[] frames;
        private bool isHitVisible;
        private float distance;
        private float knockbackHeight;
        private Vector2 attackOffset;

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
            humanSortingOrder = visual.sortingOrder;
            this.frames = frames;
            Stop();
        }


        // 유형별 표시 우선순위와 개체 색상 초기화
        public void SetUnitType(bool zombie)
        {
            // 좀비는 모든 인간 피격 및 변환 표시보다 앞에 렌더링
            isZombieVisual = zombie;
            visual.sortingOrder = humanSortingOrder + (zombie ? ZombieSortingOffset : 0);
            isHitVisible = false;
            visual.color = Color.white;
        }


        // 실제 이동 거리로 다리 프레임과 좌우 방향 갱신
        public void Walk(Vector2 direction, float travelled)
        {
            // 수직 이동에서는 이전 좌우 방향 유지
            if (direction.x != 0f)
            {
                visual.flipX = direction.x < 0f;
                ApplyVisualPosition();
            }

            // 스프라이트 교체만 사용해 원본 머리와 몸통의 픽셀 크기 유지
            distance = Mathf.Repeat(distance + travelled, StrideLength);
            int phase = Mathf.FloorToInt(distance / StrideLength * FrameSequence.Length);
            visual.sprite = frames[FrameSequence[phase]];
        }


        // 두 번째 피격 후 몸체가 옆으로 쓰러지는 연출
        public void SetFallProgress(float progress)
        {
            // 논리 위치를 유지한 채 표시 몸체만 회전
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f * Mathf.SmoothStep(0f, 1f, progress));
        }


        // 변환 진행도에 맞춰 점진적인 색상 변화와 흰색 점멸 표시
        public void SetConversionProgress(Sprite[] conversionFrames, float progress)
        {
            // 사전 생성된 공용 픽셀 프레임만 교체해 개체별 텍스처 생성 방지
            if (isZombieVisual || conversionFrames == null || conversionFrames.Length == 0)
            {
                return;
            }

            int frame = Mathf.RoundToInt(Mathf.Clamp01(progress) * (conversionFrames.Length - 1));
            visual.sprite = conversionFrames[frame];
            visual.color = isHitVisible ? HitColor : Color.white;
        }


        // 공용 좀비 프레임으로 교체 후 일어서는 변환 연출
        public void ChangeToZombie(Sprite[] zombieFrames)
        {
            // 기존 렌더러를 재사용해 중복 개체 생성 방지
            SetUnitType(true);
            frames = zombieFrames;
            knockbackHeight = 0f;
            attackOffset = Vector2.zero;
            SetHitColor(false);
            Stop();
            ApplyVisualPosition();
        }


        // 피격 중 빨간색과 원래 스프라이트 색상 전환
        public void SetHitColor(bool hit)
        {
            // 공용 텍스처 대신 개체 렌더러 색상만 변경
            isHitVisible = hit && !isZombieVisual;
            visual.color = isHitVisible ? HitColor : Color.white;
        }


        // 논리 위치를 바꾸지 않는 공격 준비와 타격 오프셋 표시
        public void SetAttackOffset(Vector2 offset)
        {
            // 좌우 반전 기준 위치에 공격 모션만 합성
            attackOffset = offset;
            ApplyVisualPosition();
        }


        // 지면 좌표와 독립된 피격 도약 높이 표시
        public void SetKnockbackHeight(float height)
        {
            // 원본 스프라이트를 변형하지 않고 수직 표시 위치만 이동
            knockbackHeight = Mathf.Max(0f, height);
            ApplyVisualPosition();
        }


        // 방향 반전과 공격 및 공중 오프셋 합성
        private void ApplyVisualPosition()
        {
            // 추적과 충돌에 사용하는 부모 지면 좌표 유지
            visual.transform.localPosition = (visual.flipX ? Vector3.right * MirrorOffset : Vector3.zero) +
                (Vector3)attackOffset + Vector3.up * knockbackHeight;
        }


        // 비활성화 시 시각 오프셋 복원
        private void OnDisable()
        {
// 중단된 피격 도약이 표시 위치에 남지 않도록 정리
            knockbackHeight = 0f;
            attackOffset = Vector2.zero;

            if (visual != null)
            {
                ApplyVisualPosition();
            }
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
