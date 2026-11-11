using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 좀비의 근접 공격 시점과 인간 피격 표시 제어
    /// </summary>
    public sealed class UnitCombat : MonoBehaviour
    {
        // 공격 준비와 회복 및 재공격 간격
        internal const float GrabRange = .5f;
        private const float DistanceTolerance = .001f;
        private const float Windup = .2f;
        private const float Recovery = .25f;
        private const float AttackInterval = 1.2f;
        private const float PursuitResumeDelay = .5f;
        private const float HitFlashDuration = .18f;
        private const float AttackVisualOffset = .15f;

        // 소유 유닛과 공격 대상 및 표시 참조
        private UnitWander movement;
        private CityMap map;
        private UnitWalkAnimation animationView;
        private UnitWander victim;
        private bool isZombie;
        private float attackTime;
        private float cooldown;
        private float pursuitDelayRemaining;
        private float flashRemaining;
        private bool isAttacking;
        private bool hasHit;
        private Vector2 attackDirection;
        public bool BlocksMovement => isAttacking || pursuitDelayRemaining > 0f;

        // 기존 이동 및 공용 지도와 전투 표시 연결
        public void Initialize(UnitWander movement, CityMap map, bool zombie, UnitWalkAnimation animationView)
        {
            // 생성 순서가 보장된 소유 참조 보관
            this.movement = movement;
            this.map = map;
            isZombie = zombie;
            this.animationView = animationView;
        }


        // 추적 중인 인간이 근접한 경우 공격 시작
        public bool TryAttack(int targetIndex, Vector2 position)
        {
            // 준비되지 않은 대상과 재공격 대기 및 건물 차폐 검사
            if (!isZombie || BlocksMovement || cooldown > 0f || !map.HumanUnits.TryGetValue(targetIndex, out var human) || human == null || !human.IsHumanTarget ||
                Vector2.Distance(position, human.Position) > GrabRange + DistanceTolerance || !UnitSight.HasLineOfSight(map, position, human.Position))
            {
                return false;
            }

            // 한 인간을 여러 좀비가 동시에 붙잡지 못하도록 소유자 지정
            if (!human.TryGrab(this))
            {
                return false;
            }

            victim = human;
            attackDirection = (human.Position - position).normalized;
            attackTime = 0f;
            hasHit = false;
            isAttacking = true;
            cooldown = AttackInterval;
            animationView.Stop();
            return true;
        }


        // 공격 준비 후 타격과 회복 및 피격 색상 복원
        private void Update()
        {
            // 초기화 전 표시 처리 생략
            if (movement == null)
            {
                return;
            }

            // 공격 종료 후 대기 동안 이동과 재공격 차단
            pursuitDelayRemaining = Mathf.Max(0f, pursuitDelayRemaining - Time.deltaTime);
            cooldown = Mathf.Max(0f, cooldown - Time.deltaTime);
            flashRemaining = Mathf.Max(0f, flashRemaining - Time.deltaTime);
            animationView.SetHitColor(flashRemaining > 0f);

            if (!isAttacking)
            {
                return;
            }

            attackTime += Time.deltaTime;
            float progress = Mathf.Clamp01(attackTime / Windup);
            float offset = attackTime < Windup ? -AttackVisualOffset * progress : AttackVisualOffset * (1f - Mathf.Clamp01((attackTime - Windup) / Recovery));
            animationView.SetAttackOffset(attackDirection * offset);

            if (!hasHit && attackTime >= Windup)
            {
                // 붙잡힌 대상에게 타격 적용 후 이동 금지 해제
                hasHit = true;

                if (victim != null && Vector2.Distance(movement.Position, victim.Position) <= GrabRange + DistanceTolerance &&
                    UnitSight.HasLineOfSight(map, movement.Position, victim.Position))
                {
                    victim.ReceiveHit(attackDirection);

                    // 두 번째 타격 콜백에서 공격이 취소되면 회복 대기를 다시 설정하지 않도록 즉시 종료
                    if (!isAttacking)
                    {
                        return;
                    }
                }

                if (victim != null)
                {
                    victim.ReleaseGrab(this);
                }
            }

            // 공격 회복과 인간 넉백 완료 후 도주 갱신 기회를 보장하고 추적 대기 시작
            if (attackTime >= Windup + Recovery && (victim == null || victim.HasResumedAfterKnockback))
            {
                isAttacking = false;
                pursuitDelayRemaining = PursuitResumeDelay;
                if (victim != null)
                {
                    victim.ReleaseGrab(this);
                }

                victim = null;
                animationView.SetAttackOffset(Vector2.zero);
            }
        }


        // 사라진 인간에 대한 공격과 붙잡힘 해제
        public void CancelHumanTarget(UnitWander human)
        {
            // 현재 공격 대상이 일치할 때만 공격 중단
            if (victim != human)
            {
                return;
            }

            victim.ReleaseGrab(this);
            victim = null;
            isAttacking = false;
            pursuitDelayRemaining = 0f;
            animationView.SetAttackOffset(Vector2.zero);
        }


        // 변환된 개체의 전투 상태를 좀비로 전환
        public void BecomeZombie()
        {
            // 인간 피격 표시와 이전 공격 상태 초기화
            isZombie = true;
            flashRemaining = 0f;
            isAttacking = false;
            hasHit = false;
            victim = null;
            pursuitDelayRemaining = 0f;
            cooldown = AttackInterval;
            animationView.SetHitColor(false);
        }


        // 인간 피격 색상 표시 시작
        public void FlashHit()
        {
            // 중복 피격 시 표시 시간 갱신
            flashRemaining = HitFlashDuration;
            animationView.SetHitColor(true);
        }


        // 비활성화 시 공격 표시와 상태 복원
        private void OnDisable()
        {
            // 공격자 비활성화나 파괴 시에도 붙잡힌 인간 즉시 해제
            if (victim != null)
            {
                victim.ReleaseGrab(this);
                victim = null;
            }

            pursuitDelayRemaining = 0f;

            // 중단된 공격의 시각 오프셋과 색상 제거
            isAttacking = false;

            if (animationView != null)
            {
                animationView.SetAttackOffset(Vector2.zero);
                animationView.SetHitColor(false);
            }
        }
    }
}
