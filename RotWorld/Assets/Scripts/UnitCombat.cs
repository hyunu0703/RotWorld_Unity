using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 좀비와 인간의 근접 공격 시점 및 피격 표시 제어
    /// </summary>
    public sealed class UnitCombat : MonoBehaviour
    {
        // 충돌 경계에서도 타격 가능한 좀비 공격 거리와 공통 준비 및 회복 간격
        internal const float GrabRange = UnitWander.SeparationDistance;
        private const float DistanceTolerance = .001f;
        private const float Windup = .2f;
        private const float Recovery = .25f;
        private const float AttackInterval = 1.2f;
        private const float PursuitResumeDelay = .5f;
        private const float HitFlashDuration = .18f;
        private const float AttackVisualOffset = .15f;

        // 인간의 좀비 반격 거리와 재공격 간격
        internal const float HumanAttackRange = 1.1f;
        private const float HumanAttackInterval = 5f;
        private const float CooperativeAttackInterval = 3f;
        private bool isCooperativeSupport;
        private float CurrentHumanAttackInterval => isCooperativeSupport ? CooperativeAttackInterval : HumanAttackInterval;

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
        private bool isCooperativeAttack;
        private Vector2 attackDirection;
        internal bool IsHumanAttackReady => !isZombie && !isAttacking && cooldown <= 0f;
        internal bool CanMoveDuringAttack => isAttacking && isCooperativeAttack && !hasHit && attackTime < Windup;
        public bool BlocksMovement => (isAttacking && !CanMoveDuringAttack) || pursuitDelayRemaining > 0f;
        private float AttackRange => isZombie ? GrabRange : HumanAttackRange;

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

            BeginAttack(human, position, AttackInterval);
            return true;
        }


        // 지정 좀비 또는 반격 범위 안의 가장 가까운 좀비에게 공격 시작
        public bool TryAttackZombie(Vector2 position, UnitWander requiredTarget = null)
        {
            // 좀비 개체와 공격 진행 및 재공격 대기 중에는 탐색 생략
            if (!IsHumanAttackReady)
            {
                return false;
            }

            // 세 인간의 지원 공격은 이동 컴포넌트의 교대 순서와 측면 접근 조건 적용
            if (requiredTarget != null && !movement.CanStartCooperativeStrike())
            {
                return false;
            }

            // 쓰러진 좀비를 제외하고 건물에 가려지지 않은 최단 거리 대상 선택
            UnitWander target = null;
            float bestDistance = HumanAttackRange + DistanceTolerance;

            foreach (var zombie in map.ZombieUnits.Values)
            {
                if (zombie == null || !zombie.IsZombieTarget || (requiredTarget != null && zombie != requiredTarget))
                {
                    continue;
                }

                float distance = Vector2.Distance(position, zombie.Position);

                if (distance > bestDistance || !UnitSight.HasLineOfSight(map, position, zombie.Position))
                {
                    continue;
                }

                target = zombie;
                bestDistance = distance;
            }

            if (target == null)
            {
                return false;
            }

            BeginAttack(target, position, CurrentHumanAttackInterval, requiredTarget != null);

            if (requiredTarget != null)
            {
                movement.NotifyCooperativeStrikeStarted();
            }

            return true;
        }


        // 지원 역할 전환 시 인간 공격 간격 변경과 남은 쿨타임 초기화
        internal void SetCooperativeSupport(bool isSupport)
        {
            // 좀비와 같은 역할의 반복 통지로 인한 쿨타임 초기화 제외
            if (isZombie || isCooperativeSupport == isSupport)
            {
                return;
            }

            isCooperativeSupport = isSupport;

            // 남은 대기를 0초로 제거해 사거리와 행동 조건 충족 시 즉시 공격 허용
            cooldown = 0f;
        }


        // 공격 대상과 방향 보관 후 준비 동작 시작
        private void BeginAttack(UnitWander target, Vector2 position, float interval, bool isCooperativeAttack = false)
        {
            // 좀비와 인간이 같은 준비 및 회복 연출을 공유하고 유형별 재공격 간격만 적용
            victim = target;
            this.isCooperativeAttack = isCooperativeAttack;
            attackDirection = (target.Position - position).normalized;
            attackTime = 0f;
            hasHit = false;
            isAttacking = true;
            cooldown = interval;

            // 이동 준비 공격은 걷기 프레임과 누적 보폭 유지
            if (!isCooperativeAttack)
            {
                animationView.Stop();
            }
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

            // 사망과 소멸 연출 중인 대상의 회복이나 파괴를 기다리지 않고 공격 종료
            if (victim == null || (isZombie ? !victim.IsHumanTarget : !victim.IsZombieTarget))
            {
                CancelAttack();
                return;
            }

            // 지원 중 새 위협이 발생하면 이동 컴포넌트 실행 순서와 무관하게 타격 취소
            if (isCooperativeAttack && !movement.CanContinueCooperativeAttack())
            {
                CancelAttack();
                return;
            }

            // 이동 공격의 준비와 타격 방향을 지정 좀비의 최신 위치에 맞춰 갱신
            if (CanMoveDuringAttack && victim != null)
            {
                attackDirection = (victim.Position - movement.Position).normalized;
            }

            attackTime += Time.deltaTime;
            float progress = Mathf.Clamp01(attackTime / Windup);
            float offset = attackTime < Windup ? -AttackVisualOffset * progress : AttackVisualOffset * (1f - Mathf.Clamp01((attackTime - Windup) / Recovery));
            animationView.SetAttackOffset(attackDirection * offset);

            if (!hasHit && attackTime >= Windup)
            {
                // 준비 시간 동안 범위를 벗어나지 않은 대상에게만 타격 적용
                hasHit = true;

                if (victim != null && Vector2.Distance(movement.Position, victim.Position) <= AttackRange + DistanceTolerance &&
                    UnitSight.HasLineOfSight(map, movement.Position, victim.Position))
                {
                    UnitWander hitTarget = victim;
                    hitTarget.ReceiveHit(attackDirection);

                    // 세 명의 협동 타격 후 반격 대상 변경과 유인자 역할 교환
                    if (!isZombie && isCooperativeAttack)
                    {
                        movement.NotifyCooperativeStrikeHit(hitTarget);
                    }

                    // 타격 콜백에서 공격이 취소되면 회복 대기를 다시 설정하지 않도록 즉시 종료
                    if (!isAttacking)
                    {
                        return;
                    }
                }

                // 좀비 공격만 인간 붙잡힘을 소유하므로 타격 직후 이동 금지 해제
                ReleaseVictimGrab();
            }

            // 공격 회복과 대상 넉백 완료 후 이동 재개
            if (attackTime >= Windup + Recovery && (victim == null || victim.HasResumedAfterKnockback))
            {
                // 좀비는 추적 대기 후 재추격하고 인간은 회복 직후 도주 재개
                isAttacking = false;
                pursuitDelayRemaining = isZombie ? PursuitResumeDelay : 0f;
                ReleaseVictimGrab();
                victim = null;
                animationView.SetAttackOffset(Vector2.zero);
            }
        }


        // 좀비 공격자가 소유한 인간 붙잡힘 해제
        private void ReleaseVictimGrab()
        {
            // 인간 공격자는 붙잡기를 사용하지 않으므로 해제 생략
            if (isZombie && victim != null)
            {
                victim.ReleaseGrab(this);
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

            CancelAttack();
        }


        // 쓰러진 좀비를 공격 중인 인간의 공격 상태 즉시 해제
        internal bool CancelZombieTarget(UnitWander zombie)
        {
            // 다른 대상과 좀비 자신의 공격 상태 보존
            if (isZombie || victim != zombie)
            {
                return false;
            }

            CancelAttack();
            return true;
        }


        // 피격이나 쓰러짐으로 진행 중인 공격 중단
        public void CancelAttack()
        {
            // 붙잡힌 인간과 공격 오프셋을 즉시 복원하고 재공격 대기는 유지
            ReleaseVictimGrab();
            victim = null;
            isAttacking = false;
            hasHit = false;
            isCooperativeAttack = false;
            pursuitDelayRemaining = 0f;
            animationView.SetAttackOffset(Vector2.zero);
        }


        // 변환된 개체의 전투 상태를 좀비로 전환
        public void BecomeZombie()
        {
            // 인간 피격 표시와 이전 공격 상태 초기화
            CancelAttack();
            isZombie = true;
            isCooperativeSupport = false;
            flashRemaining = 0f;
            cooldown = AttackInterval;
            animationView.SetHitColor(false);
        }


        // 피격 색상 표시 시작
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
            ReleaseVictimGrab();
            victim = null;
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
