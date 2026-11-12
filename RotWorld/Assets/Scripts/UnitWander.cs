using System.Collections.Generic;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 주변 배회와 좀비의 인간 추적 및 인간의 좀비 회피 이동과 피격 후 쓰러짐 제어
    /// </summary>
    public sealed class UnitWander : MonoBehaviour
    {
        // 유형별 기본 능력치와 개체 속도 편차
        [SerializeField] private float humanSpeed = 1.8f;
        [SerializeField] private float zombieSpeed = 1.2f;
        [SerializeField] private float zombieChaseSpeed = 1.85f;
        private const float MinimumSpeedVariation = .85f;
        private const float MaximumSpeedVariation = 1.15f;
        private const float MinimumRest = 1f;
        private const float MaximumRest = 6f;

        // 지도 인구와 연속 지면 위치 및 이동 경로 보관
        private CityMap map;
        private List<Vector2Int> population;
        private int populationIndex;
        private readonly List<Vector2Int> path = new List<Vector2Int>();
        private int pathIndex;
        private Vector2 position;
        private Vector2Int currentTile;

        // 개체별 난수와 이동 속도 및 걷기 표시 연결
        private System.Random random;
        private float speedVariation;
        private float restRemaining;
        private UnitWalkAnimation walkAnimation;

        // 적 감지 주기와 추적 또는 도주 경로 재계산 상태
        private const float DetectionInterval = .2f;
        private float pathRetryRemaining;
        private bool isZombie;
        private float detectionRemaining;
        private int chaseTarget = -1;
        private bool hasChasePath;
        private Vector2Int chaseDestination;

        // 도주 중 주변 좀비 좌표 재사용과 다음 타일 경계의 방향 갱신
        private readonly List<Vector2> fleeThreats = new List<Vector2>();
        private bool needsFleePathUpdate;

        // 단일 좀비에 대한 유인자와 첫 지원자 연결 및 합동 공격 거리
        private const float CooperativeRecruitRadius = 6f;
        private const float CooperativeDangerRadius = 5f;
        private const float CooperativeOrbitRadius = 4f;
        private const float OrbitDistanceTolerance = .001f;
        private float cooperativeOrbitDirection = 1f;
        private float cooperativeOrbitAngle;
        private UnitWander cooperativeHelper;
        private UnitWander cooperativeLeader;
        private UnitWander cooperativeZombie;

        // 세 인간의 양측 접근과 교대 공격 순서
        private const float CooperativeStrikeSpacing = 1.5f;
        private const float CooperativeTurnGrace = 1.5f;
        private const float CooperativeFlankAlignment = .25f;
        private const float CooperativeStagingRadius = 3f;
        private const float CooperativeApproachLeadTime = 1.25f;
        private UnitWander cooperativeSecondHelper;
        private UnitWander cooperativeNextAttacker;
        private float cooperativeNextAttackTime;
        private float cooperativeFirstSide = 1f;

        // 세 명의 협동 중 위험 동료 구출과 안전 거리 복귀
        private const float CooperativeRescueRetreatDuration = 3f;
        private const float InjuredRescuerDistancePenalty = .5f;
        private UnitWander cooperativeRescueTarget;
        private UnitWander cooperativeRescuer;
        private float cooperativeRetreatUntil;

        // 원형 도주의 장애물 사전 감지와 회피 방향 고정 및 안전 거리
        private const float OrbitObstacleMargin = .35f;
        private const float OrbitObstacleLookAhead = .9f;
        private const float OrbitObstacleReleaseDistance = .75f;
        private const float OrbitAvoidanceReleaseTravel = 1.25f;
        private bool isOrbitAvoidingObstacle;
        private float orbitAvoidanceTravelled;

        // 지원자의 지정 좀비 이동 관측과 제한된 시간의 가로채기 예측
        private const float MaximumInterceptTime = 1.5f;
        private const float MaximumMotionSampleInterval = .5f;
        private const float InterceptCalculationTolerance = .0001f;
        private Vector2 cooperativePreviousZombiePosition;
        private Vector2 cooperativeZombieVelocity;
        private float cooperativeMotionSampleTime;

        // 이동 중 출발 및 도착과 대각선 교차 영역 예약
        private bool isDirectPursuit;
        private Vector2? wanderExitTarget;
        internal const float SeparationDistance = .9f;
        private const float PursuitSpeedAdvantage = 1.05f;
        private const float MaximumSubstep = .1f;
        private readonly List<Vector2Int> reservations = new List<Vector2Int>();

        // 공격 중 이동 차단과 피격 후 비중첩 감속 및 넉백
        private UnitCombat combat;
        private UnitCombat grabOwner;
        public bool IsGrabbed => grabOwner != null;
        private bool hasBeenHit;
        private float knockbackRemaining;
        private int knockbackFinishedFrame = -1;
        public bool HasResumedAfterKnockback => knockbackElapsed >= KnockbackDuration && Time.frameCount > knockbackFinishedFrame + 1;
        private Vector2 knockbackDirection;
        private Vector2 knockbackOrigin;
        private const float InjuredSpeedMultiplier = .8f;
        private const float KnockbackDuration = .28f;
        private const float KnockbackHeight = .35f;
        private float knockbackElapsed = KnockbackDuration;
        private const float KnockbackDistance = 1f;
        public Vector2 Position => position;

        // 유형별 누적 피격 후 쓰러짐과 감염 변환 및 좀비 소멸 상태
        private const int HumanHitsToFall = 2;
        private const int ZombieHitsToFall = 5;
        private const float ZombieFadeStartDelay = 2f;
        private const float ZombieFadeEndTime = 5f;
        private const float ZombieDisintegrateDelay = 5f;
        private UnitDeathVisuals deathVisuals;
        private int hitCount;
        private bool isDowned;
        private float downedElapsed;
        private float riseRemaining;
        private Sprite[] zombieFrames;
        private Sprite[] conversionFrames;
        private const float ConversionDelay = 5f;
        private const float FallDuration = .3f;
        private const float RiseDuration = .3f;
        public bool IsHumanTarget => !isZombie && !isDowned;
        public bool IsZombieTarget => isZombie && !isDowned;

        // 기존 인구 데이터와 이동 및 걷기 표시 연결
        public void Initialize(CityMap map, bool zombie, int index, Sprite[] zombieFrames = null, Sprite[] conversionFrames = null,
            UnitDeathVisuals deathVisuals = null)
        {
            // 필수 지도와 표시 컴포넌트 및 인구 인덱스 검증
            var units = map == null ? null : zombie ? map.Zombies : map.Humans;

            if (units == null || index < 0 || index >= units.Count || !TryGetComponent(out walkAnimation))
            {
                Debug.LogError("유닛 이동 초기화에 필요한 지도, 인구 또는 걷기 표시 누락", this);
                enabled = false;
                return;
            }

            this.map = map;
            isZombie = zombie;
            walkAnimation.SetMap(map);
            walkAnimation.SetUnitType(zombie);
            this.zombieFrames = zombieFrames;
            this.conversionFrames = conversionFrames;
            this.deathVisuals = deathVisuals;
            combat = gameObject.AddComponent<UnitCombat>();
            combat.Initialize(this, map, zombie, walkAnimation);

            if (!zombie)
            {
                map.HumanUnits[index] = this;
            }
            else
            {
                map.ZombieUnits[index] = this;
            }

            population = units;
            populationIndex = index;
            currentTile = units[index];
            position = currentTile;
            PublishPosition();
            map.OccupyTile(currentTile);
            random = new System.Random(unchecked(map.Seed * 397 ^ index * 7919 ^ (zombie ? 1 : 0)));
            speedVariation = Mathf.Lerp(MinimumSpeedVariation, MaximumSpeedVariation, (float)random.NextDouble());

            // 생성 직후에는 휴식 없이 기존 배회 경로로 이동 시작
            UnitPathfinder.FindWanderPath(map, currentTile, random, path);
            PreparePathStart();

            if (path.Count == 0)
            {
                Rest();
            }
        }


        // 경로를 연속 이동하고 목적지 도착 후에만 휴식
        private void Update()
        {
            // 초기화 전과 시간 정지 중 처리 생략
            if (map == null || Time.deltaTime <= 0f)
            {
                return;
            }

            // 쓰러진 개체의 이동을 차단하고 좀비는 흰색 전환 후 소멸 및 인간은 설정 시간 후 변환
            if (isDowned)
            {
                downedElapsed += Time.deltaTime;
                walkAnimation.SetFallProgress(downedElapsed / FallDuration);

                if (isZombie)
                {
                    // 쓰러진 뒤 2초부터 소멸 직전까지 공용 프레임으로 점진적인 흰색 전환
                    float fadeProgress = Mathf.InverseLerp(ZombieFadeStartDelay, ZombieFadeEndTime, downedElapsed);
                    walkAnimation.SetDeathProgress(deathVisuals == null ? null : deathVisuals.FadeFrames, fadeProgress);

                    if (downedElapsed >= ZombieDisintegrateDelay)
                    {
                        Disintegrate();
                    }

                    return;
                }

                walkAnimation.SetConversionProgress(conversionFrames, downedElapsed / ConversionDelay);

                if (downedElapsed >= ConversionDelay)
                {
                    TryConvertToZombie();
                }

                return;
            }

            if (riseRemaining > 0f)
            {
                riseRemaining = Mathf.Max(0f, riseRemaining - Time.deltaTime);
                walkAnimation.SetFallProgress(riseRemaining / RiseDuration);
                return;
            }

            // 이동 차단 중에도 추가 위협과 사라진 참여자를 확인해 합동 공격 해제
            if (!isZombie)
            {
                ValidateCooperativeAttack();
            }

            // 합동 공격 도주자도 근접 반격 확인 및 지원자는 지정 좀비만 공격
            TryAttackNearbyZombie();

            // 공격 준비 중 붙잡힌 인간은 도주와 걷기 모션 중지
            if (IsGrabbed)
            {
                walkAnimation.Stop(cooperativeLeader == null);
                return;
            }

            if (knockbackElapsed < KnockbackDuration)
            {
                UpdateKnockback();
                return;
            }

            if (combat.BlocksMovement)
            {
                walkAnimation.Stop(cooperativeLeader == null);
                return;
            }

            // 추격 속도 보정에 사용할 인간의 실제 지형 속도 공유
            if (!isZombie)
            {
                map.HumanSpeeds[populationIndex] = humanSpeed * (hasBeenHit ? InjuredSpeedMultiplier : 1f) * speedVariation * UnitPathfinder.SpeedMultiplier(map, currentTile);
            }

            // 처치 직후 유형과 무관하게 쓰러진 상대에서 떨어지는 배회 구간 우선 실행
            if (wanderExitTarget.HasValue)
            {
                UpdateWanderExit();
                return;
            }

            // 도주자의 공격 대기 중 원형 이동 점유를 정리하고 기존 도주 경로 재계산
            if (cooperativeHelper != null && cooperativeSecondHelper == null && isDirectPursuit && !combat.IsHumanAttackReady)
            {
                ResetCooperativeMovement();
            }

            UpdateReaction();

            // 두 명은 공격 가능 시 원형 이동 및 세 명의 유인자는 원형 이동 유지
            if (cooperativeHelper != null && (combat.IsHumanAttackReady || cooperativeSecondHelper != null))
            {
                UpdateCooperativeOrbit();
                return;
            }

            // 좀비와 합동 공격 지원자는 기존 연속 추격 이동 공유
            if ((isZombie && (chaseTarget >= 0 || isDirectPursuit)) || cooperativeLeader != null)
            {
                PursueTarget();
                return;
            }

            if (pathIndex >= path.Count)
            {
                restRemaining -= Time.deltaTime;

                if (restRemaining <= 0f && chaseTarget < 0)
                {
                    UnitPathfinder.FindWanderPath(map, currentTile, random, path);
                    PreparePathStart();
                    pathIndex = 0;

                    if (path.Count == 0)
                    {
                        Rest();
                    }
                }

                return;
            }

            // 프레임 잔여 시간을 다음 구간에 넘겨 타일 경계의 멈춤 방지
            float remainingTime = Time.deltaTime;
            float travelled = 0f;
            Vector2 direction = Vector2.zero;

            while (remainingTime > 0f && pathIndex < path.Count)
            {
                Vector2Int target = path[pathIndex];
                // 구간 시작 전 전체 이동 영역 확보로 교차 및 맞교환 차단
                if (reservations.Count == 0 && !TryReserve(target))
                {
                    // 점유 변경 시 같은 프레임에 우회 경로 재탐색 후 이동 재시도
                    hasChasePath = false;
                    UpdateReaction(false);

                    if (chaseTarget >= 0 && pathIndex < path.Count && TryReserve(path[pathIndex]))
                    {
                        target = path[pathIndex];
                    }
                    else
                    {
                        path.Clear();
                        pathIndex = 0;
                        pathRetryRemaining = DetectionInterval;
                        walkAnimation.Stop();

                        if (chaseTarget < 0)
                        {
                            Rest();
                        }

                        break;
                    }
                }

                if (!isZombie)
                {
                    map.HumanDestinations[populationIndex] = target;
                }

                direction = (Vector2)target - position;
                float distance = direction.magnitude;
                // 감지 중인 좀비만 추적 기본 속도 적용 및 기존 개체 편차와 지형 배율 유지
                float baseSpeed = isZombie ? (chaseTarget >= 0 ? zombieChaseSpeed : zombieSpeed) : humanSpeed * (hasBeenHit ? InjuredSpeedMultiplier : 1f);
                float terrainSpeed = baseSpeed * speedVariation * UnitPathfinder.SpeedMultiplier(map, currentTile);
                float step = Mathf.Min(distance, remainingTime * terrainSpeed);
                Vector2 nextPosition = Vector2.MoveTowards(position, target, step);

                if (!CanPursueStep(nextPosition))
                {
                    break;
                }

                position = nextPosition;
                travelled += step;
                remainingTime -= step / terrainSpeed;

                if (step < distance)
                {
                    break;
                }

                ReleaseReservations();
                map.ReleaseTile(currentTile);
                currentTile = target;
                map.OccupyTile(currentTile);
                pathIndex++;

                UpdateReaction(false);
            }

            // 논리 좌표와 표시 위치 동기화 및 실제 이동 거리 기반 걷기 모션 갱신
            population[populationIndex] = new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y));
            PublishPosition();
            transform.localPosition = new Vector3(position.x, position.y, 0f);
            walkAnimation.Walk(direction, travelled);

            if (pathIndex >= path.Count)
            {
                Rest();
            }
        }


        // 모든 인간의 쿨타임과 행동 가능 상태를 지키는 근접 반격 시도
        private void TryAttackNearbyZombie()
        {
            // 좀비와 쓰러진 인간 및 넉백 중 인간의 반격 제외
            if (!IsHumanTarget || !isActiveAndEnabled || knockbackElapsed < KnockbackDuration)
            {
                return;
            }

            // 도주자는 일반 반격 및 지원자는 기존 지정 대상과 이동 공격 유지
            // 세 명의 유인자는 공격보다 유인 이동 우선
            if (cooperativeSecondHelper == null)
            {
                combat.TryAttackZombie(position, cooperativeLeader != null ? cooperativeZombie : null);
            }
        }


        // 주기적 적 감지와 타일 경계에서 안전한 추적 또는 도주 경로 전환
        private void UpdateReaction(bool detect = true)
        {
            // 공통 감지 주기로 인간과 좀비의 반응 갱신
            if (detect)
            {
                detectionRemaining -= Time.deltaTime;
                pathRetryRemaining -= Time.deltaTime;
            }

            bool shouldScan = detect && detectionRemaining <= 0f;

            if (shouldScan)
            {
                detectionRemaining = DetectionInterval;
                chaseTarget = isZombie ? UnitSight.FindChaseHuman(map, position, chaseTarget) : UnitSight.FindFleeZombie(map, position, chaseTarget);
                needsFleePathUpdate = !isZombie && chaseTarget >= 0;

                // 실제 추적자가 한 마리인 도주자만 주변 지원자 모집
                if (!isZombie && cooperativeLeader == null && cooperativeHelper == null && chaseTarget >= 0)
                {
                    TryStartCooperativeAttack();
                }

                // 기존 두 명의 협동을 유지한 채 빈 지원자 자리 모집
                if (!isZombie && cooperativeHelper != null && cooperativeSecondHelper == null)
                {
                    TryRecruitSecondHelper();
                }
            }

            // 지원자 추격과 두 명의 공격 가능 도주 및 세 명의 유인 이동은 일반 경로 교체 생략
            if ((isZombie && (isDirectPursuit || chaseTarget >= 0)) || cooperativeLeader != null ||
                (cooperativeHelper != null && (combat.IsHumanAttackReady || cooperativeSecondHelper != null)))
            {
                return;
            }

            // 진행 중인 인접 구간을 마친 뒤 경로 교체로 건물 모서리 관통 방지
            if (reservations.Count > 0)
            {
                return;
            }

            if (chaseTarget < 0)
            {
                if (hasChasePath)
                {
                    path.Clear();
                    pathIndex = 0;
                    hasChasePath = false;
                    Rest();
                }

                return;
            }

            Vector2Int destination = (isZombie ? map.Humans : map.Zombies)[chaseTarget];

            // 반올림 좌표 대신 인간의 현재 이동 구간 목적지 추적
            if (isZombie && map.HumanDestinations.TryGetValue(chaseTarget, out Vector2Int movingTarget))
            {
                destination = movingTarget;
            }

            if (!hasChasePath || needsFleePathUpdate || destination != chaseDestination || (pathIndex >= path.Count && pathRetryRemaining <= 0f))
            {
                // 일시적으로 막힌 경로도 주기적으로 재탐색해 영구 대기 방지
                pathRetryRemaining = DetectionInterval;

                // 좀비는 인간 위치 추적 및 인간은 주변 좀비가 없는 방향으로 도주
                if (isZombie)
                {
                    UnitPathfinder.FindTargetPath(map, currentTile, destination, path, true);
                }
                else
                {
                    UnitSight.CollectNearbyZombies(map, position, fleeThreats);
                    UnitPathfinder.FindFleePath(map, currentTile, destination, fleeThreats, path);
                    needsFleePathUpdate = false;
                }

                PreparePathStart();
                pathIndex = 0;
                chaseDestination = destination;
                hasChasePath = true;

                if (path.Count == 0)
                {
                    walkAnimation.Stop();
                }
            }
        }


        // 한 마리에게 추적당하는 도주자 주변에서 가장 가까운 지원자 연결
        private void TryStartCooperativeAttack()
        {
            // 감지된 좀비 수가 아닌 실제 추적 대상 기록으로 단일 추적자 확인
            UnitWander pursuer = null;

            foreach (var zombie in map.ZombieUnits.Values)
            {
                if (zombie == null || !zombie.IsZombieTarget || !zombie.isActiveAndEnabled || zombie.chaseTarget != populationIndex)
                {
                    continue;
                }

                if (pursuer != null)
                {
                    return;
                }

                pursuer = zombie;
            }

            if (pursuer == null || HasOtherZombieThreat(pursuer))
            {
                return;
            }

            // 첫 지원자와 추가 지원자에 동일한 모집 조건 적용
            UnitWander helper = FindAvailableCooperativeHelper(pursuer);

            if (helper == null)
            {
                return;
            }

            // 도주자 공격 중단과 지원자의 지정 좀비 추격 시작
            cooperativeHelper = helper;
            cooperativeZombie = pursuer;
            helper.cooperativeLeader = this;
            helper.cooperativeZombie = pursuer;
            helper.cooperativePreviousZombiePosition = pursuer.position;
            helper.cooperativeZombieVelocity = Vector2.zero;
            helper.cooperativeMotionSampleTime = Time.time;
            Vector2 radial = position - helper.position;
            Vector2 tangent = new Vector2(-radial.y, radial.x);
            cooperativeOrbitDirection = Vector2.Dot(tangent, position - pursuer.position) >= 0f ? 1f : -1f;
            cooperativeOrbitAngle = Mathf.Atan2(radial.y, radial.x);
            isOrbitAvoidingObstacle = false;
            orbitAvoidanceTravelled = 0f;
            combat.CancelAttack();
            helper.combat.CancelAttack();

            // 지원자만 재공격 간격 3초 전환 및 기존 쿨타임 제거
            helper.combat.SetCooperativeSupport(true);
        }


        // 중복 배정과 추가 위협이 없는 가장 가까운 지원자 조회
        private UnitWander FindAvailableCooperativeHelper(UnitWander target)
        {
            // 도주자의 6타일 이내에서 기존 협동 참여자와 행동 불가능 인간 제외
            UnitWander helper = null;
            float bestDistance = CooperativeRecruitRadius * CooperativeRecruitRadius;

            foreach (var human in map.HumanUnits.Values)
            {
                if (human == null || human == this || !human.IsHumanTarget || !human.isActiveAndEnabled ||
                    human.cooperativeLeader != null || human.cooperativeHelper != null || human.IsGrabbed ||
                    human.knockbackElapsed < KnockbackDuration)
                {
                    continue;
                }

                float distance = (human.position - position).sqrMagnitude;

                if (distance > bestDistance || target.chaseTarget == human.populationIndex || human.HasOtherZombieThreat(target))
                {
                    continue;
                }

                helper = human;
                bestDistance = distance;
            }

            return helper;
        }


        // 기존 두 인간의 협동에 두 번째 공격자 연결
        private void TryRecruitSecondHelper()
        {
            // 유효한 두 명의 협동에서만 세 번째 인간 모집
            if (cooperativeSecondHelper != null || !CanContinueCooperativeAttack())
            {
                return;
            }

            UnitWander helper = FindAvailableCooperativeHelper(cooperativeZombie);

            if (helper == null)
            {
                return;
            }

            // 기존 지원자 쪽을 유지하고 새 지원자에게 반대편 배정
            cooperativeSecondHelper = helper;
            ConfigureCooperativeHelper(helper);
            Vector2 forward = (position - cooperativeZombie.position).normalized;
            Vector2 side = new Vector2(-forward.y, forward.x);
            cooperativeFirstSide = Vector2.Dot(cooperativeHelper.position - cooperativeZombie.position, side) >= 0f ? 1f : -1f;
            cooperativeNextAttacker = cooperativeHelper;
            cooperativeNextAttackTime = Time.time + CooperativeStrikeSpacing;
            combat.CancelAttack();

            // 두 공격자의 중간을 기준으로 새 원형 유인 각도 계산
            ResetCooperativeMovement();
        }


        // 세 명의 협동에서 지정 공격 순서와 측면 접근 조건 조회
        internal bool CanStartCooperativeStrike()
        {
            // 두 명의 협동은 기존 공격 시점 유지
            UnitWander leader = cooperativeLeader;

            if (leader == null || leader.cooperativeSecondHelper == null)
            {
                return true;
            }

            // 협동 종료 조건과 현재 피추적자의 지원 공격 차단
            if (!CanContinueCooperativeAttack() || cooperativeZombie.chaseTarget == populationIndex)
            {
                return false;
            }

            // 구출자는 교대 시각과 측면 배치보다 위험 동료의 붙잡힘 해제 우선
            if (leader.cooperativeRescueTarget != null)
            {
                return leader.cooperativeRescuer == this;
            }

            if (Time.time < cooperativeRetreatUntil || !HasCooperativeStrikeTurn())
            {
                return false;
            }

            Vector2 flank = CalculateCooperativeFlankPosition(SeparationDistance);

            // 좁은 지형에서는 측면 강제 대신 기존 근접 공격 허용
            return !IsTerrainClear(position, flank) || Vector2.Dot((position - cooperativeZombie.position).normalized,
                (flank - cooperativeZombie.position).normalized) >= CooperativeFlankAlignment;
        }


        // 세 명의 참여자 중 붙잡히거나 부상 상태로 추적당하는 동료 조회
        private UnitWander FindCooperativeRescueTarget()
        {
            // 붙잡힌 동료를 우선하고 부상자의 재피격 위험을 다음 순위로 확인
            if (IsGrabbed)
            {
                return this;
            }

            if (cooperativeHelper.IsGrabbed)
            {
                return cooperativeHelper;
            }

            if (cooperativeSecondHelper.IsGrabbed)
            {
                return cooperativeSecondHelper;
            }

            if (hasBeenHit && cooperativeZombie.chaseTarget == populationIndex)
            {
                return this;
            }

            if (cooperativeHelper.hasBeenHit && cooperativeZombie.chaseTarget == cooperativeHelper.populationIndex)
            {
                return cooperativeHelper;
            }

            return cooperativeSecondHelper.hasBeenHit && cooperativeZombie.chaseTarget == cooperativeSecondHelper.populationIndex
                ? cooperativeSecondHelper : null;
        }


        // 위험 동료를 구할 수 있는 지원자의 행동 가능 상태 조회
        private bool CanRescueCooperativeHuman(UnitWander helper, UnitWander patient)
        {
            // 환자 자신과 붙잡힘 및 넉백 중인 지원자 제외
            return helper != null && helper != patient && helper.IsHumanTarget && helper.isActiveAndEnabled && !helper.IsGrabbed &&
                helper.knockbackElapsed >= KnockbackDuration;
        }


        // 공격 준비와 부상 위험을 고려한 접근 거리로 구출자 선택
        private UnitWander FindCooperativeRescuer(UnitWander patient)
        {
            // 유인자를 제외한 두 지원자의 구출 가능 상태 확인
            UnitWander first = CanRescueCooperativeHuman(cooperativeHelper, patient) ? cooperativeHelper : null;
            UnitWander second = CanRescueCooperativeHuman(cooperativeSecondHelper, patient) ? cooperativeSecondHelper : null;

            if (first == null || second == null)
            {
                return first != null ? first : second;
            }

            // 즉시 타격 가능한 지원자를 우선하고 부상 위험을 더한 접근 거리 비교
            if (first.combat.IsHumanAttackReady != second.combat.IsHumanAttackReady)
            {
                return first.combat.IsHumanAttackReady ? first : second;
            }

            float firstDistance = Vector2.Distance(first.position, cooperativeZombie.position) + (first.hasBeenHit ? InjuredRescuerDistancePenalty : 0f);
            float secondDistance = Vector2.Distance(second.position, cooperativeZombie.position) + (second.hasBeenHit ? InjuredRescuerDistancePenalty : 0f);
            return firstDistance <= secondDistance ? first : second;
        }


        // 참여자의 위험 상태에 따라 협동 공격과 구출 우선 상태 갱신
        private void RefreshCooperativeRescue()
        {
            // 두 명의 기존 협동에는 구출조 상태 미적용
            UnitWander leader = cooperativeLeader != null ? cooperativeLeader : cooperativeHelper != null ? this : null;

            if (leader == null || leader.cooperativeSecondHelper == null)
            {
                return;
            }

            UnitWander patient = leader.FindCooperativeRescueTarget();
            UnitWander rescuer = null;

            if (patient != null)
            {
                // 진행 중인 구출 공격의 소유자는 유지하고 행동 불가능 구출자만 교체
                bool canKeepRescuer = patient == leader.cooperativeRescueTarget && leader.CanRescueCooperativeHuman(leader.cooperativeRescuer, patient);
                rescuer = canKeepRescuer ? leader.cooperativeRescuer : leader.FindCooperativeRescuer(patient);

                // 구출자의 쿨타임 대기 중 다른 지원자가 준비되면 즉시 구출 기회 이전
                if (canKeepRescuer && !rescuer.combat.IsHumanAttackReady && !rescuer.combat.BlocksMovement && !rescuer.combat.CanMoveDuringAttack)
                {
                    UnitWander readyHelper = leader.FindCooperativeRescuer(patient);

                    if (readyHelper != null && readyHelper.combat.IsHumanAttackReady)
                    {
                        rescuer = readyHelper;
                    }
                }
            }

            if (patient == leader.cooperativeRescueTarget && rescuer == leader.cooperativeRescuer)
            {
                return;
            }

            leader.cooperativeRescueTarget = patient;
            leader.cooperativeRescuer = rescuer;

            // 구출 중 다른 지원자의 준비 공격을 취소해 중복 진입과 재추적 방지
            if (patient != null)
            {
                if (leader.cooperativeHelper != rescuer)
                {
                    leader.cooperativeHelper.combat.CancelAttack();
                }

                if (leader.cooperativeSecondHelper != rescuer)
                {
                    leader.cooperativeSecondHelper.combat.CancelAttack();
                }
            }
        }


        // 현재 시각 또는 접근 준비 시각의 협동 공격 차례 조회
        private bool HasCooperativeStrikeTurn(float anticipation = 0f)
        {
            // 공격 가능 시각 전의 이동 준비와 실제 공격 허용 시각 구분
            UnitWander leader = cooperativeLeader;
            float readyTime = Time.time + anticipation;
            return readyTime >= leader.cooperativeNextAttackTime &&
                (leader.cooperativeNextAttacker == this || readyTime >= leader.cooperativeNextAttackTime + CooperativeTurnGrace);
        }


        // 공격 시작을 다음 공격자의 차례와 최소 간격에 반영
        internal void NotifyCooperativeStrikeStarted()
        {
            // 두 명의 협동에는 교대 제약 미적용
            if (cooperativeLeader == null || cooperativeLeader.cooperativeSecondHelper == null)
            {
                return;
            }

            UnitWander leader = cooperativeLeader;
            leader.cooperativeNextAttacker = leader.cooperativeHelper == this ? leader.cooperativeSecondHelper : leader.cooperativeHelper;
            leader.cooperativeNextAttackTime = Time.time + CooperativeStrikeSpacing;
        }


        // 세 명의 협동 타격에 대한 좀비의 반격 대상과 새 유인자 지정
        internal void NotifyCooperativeStrikeHit(UnitWander target)
        {
            // 두 명의 기존 전략과 처치된 좀비의 종료 흐름 보존
            if (cooperativeLeader == null || cooperativeLeader.cooperativeSecondHelper == null || target == null ||
                !target.IsZombieTarget || target != cooperativeZombie)
            {
                return;
            }

            // 실제 타격자를 반격 대상으로 삼아 유인 부담을 세 인간에게 분산
            UnitWander patient = cooperativeLeader.cooperativeRescueTarget;

            if (patient != null)
            {
                // 구출된 인간의 재공격 진입을 잠시 막고 안전 거리 복귀 시간 확보
                patient.cooperativeRetreatUntil = Time.time + CooperativeRescueRetreatDuration;
            }

            target.chaseTarget = populationIndex;
            target.detectionRemaining = DetectionInterval;
            cooperativeLeader.TransferCooperativeLeadership(this);
        }


        // 유인자와 좀비를 잇는 축의 양옆 대기 또는 공격 지점 계산
        private Vector2 CalculateCooperativeFlankPosition(float distance)
        {
            // 첫 지원자와 추가 지원자에게 반대 방향의 요청 거리 배정
            UnitWander leader = cooperativeLeader;
            Vector2 forward = (leader.position - cooperativeZombie.position).normalized;

            if (forward.sqrMagnitude == 0f)
            {
                forward = Vector2.up;
            }

            float side = leader.cooperativeHelper == this ? leader.cooperativeFirstSide : -leader.cooperativeFirstSide;
            return cooperativeZombie.position + new Vector2(-forward.y, forward.x) * side * distance;
        }


        // 참여자에게 적용할 추가 좀비의 근접 또는 추적 위협 조회
        private bool HasOtherZombieThreat(UnitWander target)
        {
            // 건물 차폐와 무관한 5타일 거리 및 다른 좀비의 추적 기록 검사
            foreach (var zombie in map.ZombieUnits.Values)
            {
                if (zombie == null || zombie == target || !zombie.IsZombieTarget || !zombie.isActiveAndEnabled)
                {
                    continue;
                }

                if ((zombie.position - position).sqrMagnitude <= CooperativeDangerRadius * CooperativeDangerRadius ||
                    zombie.chaseTarget == populationIndex)
                {
                    return true;
                }
            }

            return false;
        }


        // 합동 공격 참여자와 지정 좀비 및 추가 위협의 유효성 조회
        internal bool CanContinueCooperativeAttack()
        {
            // 지원자도 직접 조건을 확인해 Update 실행 순서에 따른 공격 지속 차단
            UnitWander leader = cooperativeLeader != null ? cooperativeLeader : cooperativeHelper != null ? this : null;

            if (leader == null)
            {
                return false;
            }

            UnitWander helper = leader.cooperativeHelper;
            UnitWander secondHelper = leader.cooperativeSecondHelper;
            UnitWander target = leader.cooperativeZombie;

            // 세 명은 어느 참여자가 추적당해도 협동 유지 후 유인자 역할 교환
            return leader.IsHumanTarget && leader.isActiveAndEnabled && helper != null && helper.IsHumanTarget &&
                helper.isActiveAndEnabled && target != null && target.IsZombieTarget && target.isActiveAndEnabled &&
                (target.chaseTarget == leader.populationIndex || (secondHelper != null &&
                    (target.chaseTarget == helper.populationIndex || target.chaseTarget == secondHelper.populationIndex))) &&
                !leader.HasOtherZombieThreat(target) && !helper.HasOtherZombieThreat(target) &&
                (secondHelper == null || (secondHelper.IsHumanTarget && secondHelper.isActiveAndEnabled && !secondHelper.HasOtherZombieThreat(target)));
        }


        // 추가 위협이나 참여자 상실 시 합동 공격 해제
        private void ValidateCooperativeAttack()
        {
            // 양방향 연결의 소유 도주자를 통해 동일한 종료 처리 적용
            UnitWander leader = cooperativeLeader != null ? cooperativeLeader : cooperativeHelper != null ? this : null;

            // 세 명의 협동에서 좀비가 다른 참여자를 추적하면 새 유인자로 전환
            if (leader != null && leader.cooperativeSecondHelper != null && CanContinueCooperativeAttack() &&
                leader.cooperativeZombie.chaseTarget != leader.populationIndex)
            {
                UnitWander nextLeader = leader.cooperativeZombie.chaseTarget == leader.cooperativeHelper.populationIndex
                    ? leader.cooperativeHelper : leader.cooperativeSecondHelper;
                leader.TransferCooperativeLeadership(nextLeader);
                leader = nextLeader;
            }

            if (leader != null && !CanContinueCooperativeAttack())
            {
                leader.StopCooperativeAttack();
            }
            else if (leader != null)
            {
                // 이동 차단 중에도 구출 상태 갱신 후 지원자의 이동 관측 유지
                RefreshCooperativeRescue();

                if (cooperativeLeader != null)
                {
                    UpdateCooperativeTargetMotion();
                }
            }
        }


        // 추적 대상이 된 공격자에게 세 명의 유인자 역할 이전
        private void TransferCooperativeLeadership(UnitWander nextLeader)
        {
            // 기존 유인자를 새 공격자로 배치하고 다른 공격자의 자리 유지
            UnitWander first = cooperativeHelper == nextLeader ? this : cooperativeHelper;
            UnitWander second = cooperativeSecondHelper == nextLeader ? this : cooperativeSecondHelper;
            UnitWander target = cooperativeZombie;
            cooperativeHelper = null;
            cooperativeSecondHelper = null;
            cooperativeNextAttacker = null;
            cooperativeRescueTarget = null;
            cooperativeRescuer = null;
            nextLeader.cooperativeLeader = null;
            nextLeader.cooperativeHelper = first;
            nextLeader.cooperativeSecondHelper = second;
            nextLeader.cooperativeZombie = target;
            nextLeader.cooperativeNextAttacker = first;
            nextLeader.cooperativeNextAttackTime = Time.time + CooperativeStrikeSpacing;
            nextLeader.cooperativeRescueTarget = null;
            nextLeader.cooperativeRescuer = null;
            nextLeader.combat.CancelAttack();
            nextLeader.combat.SetCooperativeSupport(false);
            nextLeader.ResetCooperativeMovement();

            // 새 유인자 기준의 공격 연결과 관측 속도 초기화
            nextLeader.ConfigureCooperativeHelper(first);
            nextLeader.ConfigureCooperativeHelper(second);
            Vector2 forward = (nextLeader.position - target.position).normalized;
            Vector2 side = new Vector2(-forward.y, forward.x);
            nextLeader.cooperativeFirstSide = Vector2.Dot(first.position - target.position, side) >= 0f ? 1f : -1f;
        }


        // 역할 교환 후 지원자의 연결과 이동 및 공격 상태 갱신
        private void ConfigureCooperativeHelper(UnitWander helper)
        {
            // 새 유인자와 지정 좀비를 전달하고 이전 공격 및 예측 속도 폐기
            helper.cooperativeLeader = this;
            helper.cooperativeZombie = cooperativeZombie;
            helper.cooperativePreviousZombiePosition = cooperativeZombie.position;
            helper.cooperativeZombieVelocity = Vector2.zero;
            helper.cooperativeMotionSampleTime = Time.time;
            helper.combat.CancelAttack();
            helper.combat.SetCooperativeSupport(true);
            helper.ResetCooperativeMovement();
        }


        // 양방향 합동 공격 연결 해제 후 지원자의 기존 감지와 도주 복구
        private void StopCooperativeAttack()
        {
            // 유인자와 두 지원자의 참조를 함께 정리해 재배정 허용
            UnitWander helper = cooperativeHelper;
            UnitWander secondHelper = cooperativeSecondHelper;
            cooperativeHelper = null;
            cooperativeSecondHelper = null;
            cooperativeNextAttacker = null;
            cooperativeRescueTarget = null;
            cooperativeRescuer = null;
            cooperativeZombie = null;
            combat.CancelAttack();
            ResetCooperativeMovement();
            DetachCooperativeHelper(helper);
            DetachCooperativeHelper(secondHelper);
        }


        // 협동에서 빠진 지원자의 일반 공격 간격과 감지 이동 복구
        private void DetachCooperativeHelper(UnitWander helper)
        {
            // 빈 두 번째 자리나 이미 제거된 참여자 제외
            if (helper == null)
            {
                return;
            }

            helper.cooperativeLeader = null;
            helper.cooperativeZombie = null;
            helper.cooperativeZombieVelocity = Vector2.zero;
            helper.cooperativeRetreatUntil = 0f;
            helper.combat.CancelAttack();

            // 지원 종료 시 재공격 간격 5초 복구 및 남은 쿨타임 제거
            helper.combat.SetCooperativeSupport(false);
            helper.ResetCooperativeMovement();
        }


        // 합동 공격의 연속 이동 종료 후 기존 도주 경로 재계산 준비
        private void ResetCooperativeMovement()
        {
            // 이전 예약과 소수점 위치의 점유를 정리한 뒤 감지 즉시 갱신
            ReleaseReservations();
            map.ReleaseTile(currentTile);
            currentTile = Vector2Int.RoundToInt(position);
            map.OccupyTile(currentTile);
            isDirectPursuit = false;
            isOrbitAvoidingObstacle = false;
            orbitAvoidanceTravelled = 0f;
            path.Clear();
            pathIndex = 0;
            hasChasePath = false;
            detectionRemaining = 0f;
            restRemaining = 0f;
            wanderExitTarget = null;
        }


        // 한 지원자 또는 두 공격자의 중간점 중심으로 반경 4타일 원형 도주
        private void UpdateCooperativeOrbit()
        {
            // 두 명은 기존 지원자 중심 유지 및 세 명은 두 공격자의 중간점 사용
            Vector2 center = cooperativeSecondHelper == null ? cooperativeHelper.position
                : (cooperativeHelper.position + cooperativeSecondHelper.position) * .5f;

            // 기존 타일 경로 예약을 반환하고 실제 좌표 기반 이동으로 전환
            if (!isDirectPursuit)
            {
                ReleaseReservations();
                map.ReleaseTile(currentTile);
                currentTile = Vector2Int.RoundToInt(position);
                map.OccupyTile(currentTile);
                isDirectPursuit = true;
                path.Clear();
                pathIndex = 0;
                hasChasePath = false;

                // 쿨타임 중 일반 도주로 달라진 위치에서 원형 회전 각도 재설정
                Vector2 radial = position - center;
                cooperativeOrbitAngle = Mathf.Atan2(radial.y, radial.x);
            }

            float speed = humanSpeed * (hasBeenHit ? InjuredSpeedMultiplier : 1f) * speedVariation * UnitPathfinder.SpeedMultiplier(map, currentTile);
            float remaining = speed * Time.deltaTime;
            float travelled = 0f;
            Vector2 direction = Vector2.zero;

            while (remaining > 0f)
            {
                float step = Mathf.Min(remaining, MaximumSubstep);
                Vector2 radial = position - center;

                if (radial.sqrMagnitude == 0f)
                {
                    radial = position - cooperativeZombie.position;
                    radial = radial.sqrMagnitude > 0f ? radial : Vector2.right;
                }

                // 앞쪽 지형이 막힐 때 한 번만 반전하고 장애물 이탈 전까지 방향 고정
                float forwardAngle = cooperativeOrbitAngle + cooperativeOrbitDirection * step / CooperativeOrbitRadius;
                Vector2 forward = CalculateCooperativeOrbitStep(position, center, step, forwardAngle, CooperativeOrbitRadius) - position;
                bool isAheadClear = HasOrbitTerrainClearance(position, position + forward.normalized * OrbitObstacleLookAhead, OrbitObstacleMargin);

                if (isOrbitAvoidingObstacle)
                {
                    if (orbitAvoidanceTravelled >= OrbitAvoidanceReleaseTravel &&
                        GetOrbitObstacleDistance(position) >= OrbitObstacleReleaseDistance && isAheadClear)
                    {
                        isOrbitAvoidingObstacle = false;
                    }
                }
                else if (!isAheadClear)
                {
                    cooperativeOrbitDirection = -cooperativeOrbitDirection;
                    cooperativeOrbitAngle = Mathf.Atan2(radial.y, radial.x);
                    isOrbitAvoidingObstacle = true;
                    orbitAvoidanceTravelled = 0f;
                }

                // 회피 중 접선 우선으로 벽에서 벗어나고 일반 이동은 원주 우선으로 탐색
                const int OrbitRadiusAttempts = 4;
                const int OrbitSteeringAttempts = 5;
                const float InnerOrbitScale = .75f;
                const float SteeringAngle = 30f;
                bool canMove = false;
                Vector2 next = position;
                float nextAngle = cooperativeOrbitAngle;
                Vector2 tangent = new Vector2(-radial.y, radial.x).normalized * cooperativeOrbitDirection;

                for (int attempt = 0; attempt < OrbitRadiusAttempts + OrbitSteeringAttempts; attempt++)
                {
                    float startAngle = isOrbitAvoidingObstacle ? Mathf.Atan2(radial.y, radial.x) : cooperativeOrbitAngle;
                    float angle = Mathf.Repeat(startAngle + cooperativeOrbitDirection * step / CooperativeOrbitRadius, Mathf.PI * 2f);
                    Vector2 candidate;

                    bool isSteeringCandidate = isOrbitAvoidingObstacle ? attempt < OrbitSteeringAttempts : attempt >= OrbitRadiusAttempts;

                    if (!isSteeringCandidate)
                    {
                        int radiusAttempt = isOrbitAvoidingObstacle ? attempt - OrbitSteeringAttempts : attempt;
                        float radius = radiusAttempt == 0 ? CooperativeOrbitRadius : Mathf.Max(SeparationDistance,
                            Mathf.Min(radial.magnitude, CooperativeOrbitRadius) * Mathf.Pow(InnerOrbitScale, radiusAttempt));
                        candidate = CalculateCooperativeOrbitStep(position, center, step, angle, radius);
                    }
                    else
                    {
                        // 같은 회전 방향을 유지하는 접선 기준 좌우 최대 60도 우회
                        int steering = isOrbitAvoidingObstacle ? attempt : attempt - OrbitRadiusAttempts;
                        float offset = (steering % 2 == 0 ? -1f : 1f) * ((steering + 1) / 2) * SteeringAngle * Mathf.Deg2Rad;
                        Vector2 heading = new Vector2(tangent.x * Mathf.Cos(offset) - tangent.y * Mathf.Sin(offset),
                            tangent.x * Mathf.Sin(offset) + tangent.y * Mathf.Cos(offset));
                        candidate = position + heading * step;
                        angle = Mathf.Atan2(candidate.y - center.y, candidate.x - center.x);
                    }

                    // 반대 회전으로 되돌아가는 후보와 원 범위 이탈 및 안전 거리 침범 제외
                    float allowedRadius = Mathf.Max(CooperativeOrbitRadius, Vector2.Distance(position, center));

                    if (Vector2.Dot(candidate - position, tangent) < -OrbitDistanceTolerance ||
                        Vector2.Distance(candidate, center) > allowedRadius + OrbitDistanceTolerance || !IsOrbitStepSafe(candidate))
                    {
                        continue;
                    }

                    next = candidate;
                    nextAngle = angle;
                    canMove = true;
                    break;
                }

                if (!canMove || next == position)
                {
                    break;
                }

                // 기존 충돌 검사와 점유 갱신을 유지한 소단위 연속 이동
                float distance = Vector2.Distance(position, next);
                map.ReleaseTile(currentTile);
                currentTile = Vector2Int.RoundToInt(next);
                map.OccupyTile(currentTile);
                direction = next - position;
                position = next;
                cooperativeOrbitAngle = nextAngle;
                travelled += distance;

                if (isOrbitAvoidingObstacle)
                {
                    orbitAvoidanceTravelled += distance;
                }

                remaining = Mathf.Max(0f, remaining - step);
            }

            // 추적자에게 실제 도주 좌표와 다음 이동 위치 전달 및 걷기 표시 갱신
            population[populationIndex] = currentTile;
            map.HumanDestinations[populationIndex] = currentTile;
            PublishPosition();
            transform.localPosition = new Vector3(position.x, position.y, 0f);

            if (travelled > 0f)
            {
                walkAnimation.Walk(direction, travelled);
            }
            else
            {
                walkAnimation.Stop();
            }
        }


        // 원형 도주의 실제 충돌과 장애물 여유 거리 및 앞쪽 안전 구간 확인
        private bool IsOrbitStepSafe(Vector2 candidate)
        {
            // 지형 관통과 유닛 겹침은 기존 충돌 검사로 차단
            if (!CanPursueStep(candidate))
            {
                return false;
            }

            Vector2 heading = (candidate - position).normalized;
            Vector2 ahead = candidate + heading * OrbitObstacleLookAhead;

            if (HasOrbitTerrainClearance(position, candidate, OrbitObstacleMargin) &&
                HasOrbitTerrainClearance(candidate, ahead, OrbitObstacleMargin))
            {
                return true;
            }

            // 이미 안전 거리 안에 들어온 경우 장애물에서 멀어지는 탈출 이동만 허용
            float currentDistance = GetOrbitObstacleDistance(position);
            return currentDistance < OrbitObstacleMargin &&
                GetOrbitObstacleDistance(candidate) > currentDistance + OrbitDistanceTolerance;
        }


        // 몸체 이동 영역에 여유 폭을 더해 건물과 통행 불가 지형의 사전 접근 차단
        private bool HasOrbitTerrainClearance(Vector2 from, Vector2 to, float margin)
        {
            // 기존 몸체 경계를 확장한 이동 영역과 지도 경계 및 모든 장애물 타일 검사
            Rect body = UnitWalkAnimation.BodyBounds;
            int minX = Mathf.FloorToInt(Mathf.Min(from.x, to.x) + body.xMin - margin);
            int maxX = Mathf.CeilToInt(Mathf.Max(from.x, to.x) + body.xMax + margin) - 1;
            int minY = Mathf.FloorToInt(Mathf.Min(from.y, to.y) + body.yMin - margin);
            int maxY = Mathf.CeilToInt(Mathf.Max(from.y, to.y) + body.yMax + margin) - 1;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (!UnitPathfinder.CanOccupy(map, new Vector2Int(x, y)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }


        // 장애물 이탈 판단과 밀착 상태 탈출에 사용할 몸체 기준 최단 거리 조회
        private float GetOrbitObstacleDistance(Vector2 point)
        {
            // 회피 해제 거리 안의 통행 불가 타일과 지도 밖 영역을 장애물로 취급
            Rect body = UnitWalkAnimation.BodyBounds;
            float left = point.x + body.xMin;
            float right = point.x + body.xMax;
            float bottom = point.y + body.yMin;
            float top = point.y + body.yMax;
            float nearest = OrbitObstacleReleaseDistance;
            int minX = Mathf.FloorToInt(left - OrbitObstacleReleaseDistance);
            int maxX = Mathf.FloorToInt(right + OrbitObstacleReleaseDistance);
            int minY = Mathf.FloorToInt(bottom - OrbitObstacleReleaseDistance);
            int maxY = Mathf.FloorToInt(top + OrbitObstacleReleaseDistance);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (UnitPathfinder.CanOccupy(map, new Vector2Int(x, y)))
                    {
                        continue;
                    }

                    float dx = Mathf.Max(0f, Mathf.Max(x - right, left - (x + 1f)));
                    float dy = Mathf.Max(0f, Mathf.Max(y - top, bottom - (y + 1f)));
                    nearest = Mathf.Min(nearest, Mathf.Sqrt(dx * dx + dy * dy));
                }
            }

            return nearest;
        }


        // 누적 회전 각도의 원주 목표를 향한 속도 제한 이동 좌표 계산
        private static Vector2 CalculateCooperativeOrbitStep(Vector2 position, Vector2 center, float step,
            float angle, float targetRadius)
        {
            // 지원자 최신 위치에 저장된 각도의 원주 좌표를 합성해 움직이는 중심 추적
            Vector2 target = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * targetRadius;

            // 원 안쪽과 바깥쪽 모두 기존 이동 속도로 원주에 접근 및 순간 위치 이동 방지
            return Vector2.MoveTowards(position, target, step);
        }


        // 넉백 후 소수점 좌표에서 건물 모서리를 침범하는 경로 시작 구간 보정
        private void PreparePathStart()
        {
            // 첫 목적지 직선이 몸체 충돌에 막힐 때 안전한 현재 타일 중심부터 경유
            if (path.Count > 0 && position != (Vector2)currentTile && !IsTerrainClear(position, path[0]) &&
                IsTerrainClear(position, currentTile))
            {
                path.Insert(0, currentTile);
            }
        }


        // 공격 준비 동안 인간 이동을 금지하는 붙잡기 시작
        public bool TryGrab(UnitCombat owner)
        {
            // 기존 소유자와 비활성 대상 거부 및 넉백 후 도주 갱신 기회 보장
            if (!IsHumanTarget || !isActiveAndEnabled || owner == null || IsGrabbed || !HasResumedAfterKnockback)
            {
                return false;
            }

            grabOwner = owner;
            RefreshCooperativeRescue();
            ReleaseReservations();
            map.ReleaseTile(currentTile);
            currentTile = Vector2Int.RoundToInt(position);
            map.OccupyTile(currentTile);
            path.Clear();
            pathIndex = 0;
            hasChasePath = false;
            walkAnimation.Stop();
            PublishPosition();
            return true;
        }


        // 타격 또는 공격 중단 후 소유자의 붙잡힘만 해제
        public void ReleaseGrab(UnitCombat owner)
        {
            // 다른 공격자의 붙잡힘을 임의 해제하지 않도록 소유자 확인
            if (grabOwner != owner)
            {
                return;
            }

            grabOwner = null;
            detectionRemaining = 0f;
            restRemaining = 0f;
        }

        // 피격 누적과 지정 거리 넉백 시작 및 인간의 최초 감속
        public void ReceiveHit(Vector2 direction)
        {
            // 쓰러진 개체와 초기화 전 피격 요청 무시
            if (isDowned || map == null)
            {
                return;
            }

            // 피격 시 진행 중인 자신의 공격은 중단
            combat.CancelAttack();
            hitCount++;

            if (hitCount >= (isZombie ? ZombieHitsToFall : HumanHitsToFall))
            {
                FallDown();
                return;
            }

            // 감속은 인간만 적용하고 좀비는 넉백 후 추적 또는 배회 재개
            if (!isZombie)
            {
                hasBeenHit = true;
                cooperativeRetreatUntil = Time.time + CooperativeRescueRetreatDuration;
                RefreshCooperativeRescue();
            }

            wanderExitTarget = null;
            ReleaseReservations();
            map.ReleaseTile(currentTile);
            currentTile = Vector2Int.RoundToInt(position);
            map.OccupyTile(currentTile);
            path.Clear();
            pathIndex = 0;
            hasChasePath = false;
            knockbackDirection = direction.sqrMagnitude > 0f ? direction.normalized : Vector2.right;
            knockbackOrigin = position;
            knockbackRemaining = KnockbackDistance;
            knockbackElapsed = 0f;
            walkAnimation.SetKnockbackHeight(0f);
            knockbackFinishedFrame = -1;
            combat.FlashHit();
            walkAnimation.Stop();
        }


        // 마지막 피격 후 이동과 추가 타격 중단 및 쓰러짐 시작
        private void FallDown()
        {
            // 좀비는 별도 소멸 흐름으로 분기
            if (isZombie)
            {
                FallDownZombie();
                return;
            }

            // 쓰러진 참여자의 합동 공격 연결 즉시 해제
            ReleaseCooperativeAttack();

            // 쓰러진 인간의 이동 예약과 타일 점유 및 실제 충돌 좌표 반환
            isDowned = true;
            downedElapsed = 0f;
            grabOwner = null;
            knockbackElapsed = KnockbackDuration;
            knockbackFinishedFrame = Time.frameCount;
            ReleaseReservations();
            map.ReleaseTile(currentTile);
            map.HumanPositions.Remove(populationIndex);
            path.Clear();
            pathIndex = 0;
            hasChasePath = false;
            map.InactiveHumans.Add(populationIndex);
            map.HumanSpeeds[populationIndex] = 0f;
            walkAnimation.SetDownedSortingOrder();
            walkAnimation.SetKnockbackHeight(0f);
            walkAnimation.Stop();
            walkAnimation.SetConversionProgress(conversionFrames, 0f);
            combat.FlashHit();

            foreach (var zombie in map.ZombieUnits.Values)
            {
                if (zombie != null)
                {
                    zombie.ForgetHuman(this, populationIndex);
                }
            }
        }


        // 다섯 번째 피격 후 좀비의 추적과 공격 중단 및 쓰러짐 시작
        private void FallDownZombie()
        {
            // 쓰러진 좀비의 이동 예약과 타일 점유 및 실제 충돌 좌표 반환
            isDowned = true;
            downedElapsed = 0f;
            knockbackElapsed = KnockbackDuration;
            knockbackFinishedFrame = Time.frameCount;
            ReleaseReservations();
            map.ReleaseTile(currentTile);
            map.ZombiePositions.Remove(populationIndex);
            path.Clear();
            pathIndex = 0;
            hasChasePath = false;
            chaseTarget = -1;
            isDirectPursuit = false;
            wanderExitTarget = null;

            // 감지와 인간 반격 및 인구 표시에서 제외
            map.InactiveZombies.Add(populationIndex);

            // 소멸 연출과 분리해 관련 인간의 공격과 협동 추적을 즉시 해제
            foreach (var human in map.HumanUnits.Values)
            {
                if (human != null)
                {
                    human.ForgetZombie(this, populationIndex);
                }
            }

            map.NotifyPopulationChanged();

            // 피격 색상 후 흰색 전환 시작 프레임 적용
            walkAnimation.SetDownedSortingOrder();
            walkAnimation.SetKnockbackHeight(0f);
            walkAnimation.Stop();
            walkAnimation.SetDeathProgress(deathVisuals == null ? null : deathVisuals.FadeFrames, 0f);
            combat.FlashHit();
        }


        // 흰색 전환을 마친 좀비를 먼지 연출로 교체 후 제거
        private void Disintegrate()
        {
            // 충돌 해제를 마친 시체를 독립 먼지 연출로 교체
            walkAnimation.Disintegrate(deathVisuals);
            enabled = false;
            Destroy(gameObject);
        }


        // 제거되거나 변환되는 인간 추적 해제와 배회 복귀
        private void ForgetHuman(UnitWander human, int humanIndex)
        {
            // 다른 인간을 추적 중인 개체 상태 보존
            if (chaseTarget != humanIndex)
            {
                return;
            }

            combat.CancelHumanTarget(human);
            ReleaseReservations();
            chaseTarget = -1;
            hasChasePath = false;
            isDirectPursuit = false;
            path.Clear();
            pathIndex = 0;
            detectionRemaining = MinimumRest;
            restRemaining = 0f;
            walkAnimation.Stop();
            BeginWanderExit(human.Position);
        }


        // 쓰러진 좀비의 공격과 추적 및 협동 연결 해제 후 즉시 배회 준비
        private void ForgetZombie(UnitWander zombie, int zombieIndex)
        {
            // 살아 있는 인간 중 해당 좀비와 관계된 개체만 상태 전환
            if (!IsHumanTarget)
            {
                return;
            }

            bool wasCooperating = cooperativeZombie == zombie;
            bool wasAttacking = combat.CancelZombieTarget(zombie);

            if (!wasCooperating && !wasAttacking && chaseTarget != zombieIndex)
            {
                return;
            }

            if (wasCooperating)
            {
                // 연결 해제 전에 세 참여자를 보관해 마지막 타격자의 실행 순서와 무관하게 복귀
                UnitWander leader = cooperativeLeader != null ? cooperativeLeader : this;
                UnitWander helper = leader.cooperativeHelper;
                UnitWander secondHelper = leader.cooperativeSecondHelper;
                leader.StopCooperativeAttack();

                if (leader != this && leader.IsHumanTarget)
                {
                    leader.ResumeAfterZombieDefeat(zombie);
                }

                if (helper != null && helper != this && helper.IsHumanTarget)
                {
                    helper.ResumeAfterZombieDefeat(zombie);
                }

                if (secondHelper != null && secondHelper != this && secondHelper.IsHumanTarget)
                {
                    secondHelper.ResumeAfterZombieDefeat(zombie);
                }
            }

            ResumeAfterZombieDefeat(zombie);
        }


        // 좀비 처치 직후 다른 위협 확인과 배회 이탈 구간 준비
        private void ResumeAfterZombieDefeat(UnitWander zombie)
        {
            // 다른 좀비가 보이면 도주 우선 및 위협이 없으면 시체와 분리되는 이동 시작
            ResetCooperativeMovement();
            needsFleePathUpdate = false;
            wanderExitTarget = null;
            chaseTarget = UnitSight.FindFleeZombie(map, position, -1);

            if (chaseTarget >= 0)
            {
                return;
            }

            BeginWanderExit(zombie.Position);

            if (!wanderExitTarget.HasValue)
            {
                UnitPathfinder.FindWanderPath(map, currentTile, random, path);
                PreparePathStart();
            }
        }


        // 쓰러진 상대 가까이에서 충돌 없이 빠져나갈 배회 시작 구간 선택
        private void BeginWanderExit(Vector2 fallenPosition)
        {
            // 근접 시체를 벗어나 일반 배회를 시작할 수 있는 빈 타일까지 이탈
            const int SearchRadius = 2;
            Vector2 away = position - fallenPosition;
            float bestScore = float.PositiveInfinity;
            wanderExitTarget = null;

            for (int y = -SearchRadius; y <= SearchRadius; y++)
            {
                for (int x = -SearchRadius; x <= SearchRadius; x++)
                {
                    Vector2 candidate = currentTile + new Vector2Int(x, y);
                    Vector2 delta = candidate - position;

                    // 현재 타일 중심으로의 짧은 복귀와 다른 개체가 점유한 도착점 제외
                    if (delta.sqrMagnitude < SeparationDistance * SeparationDistance || Vector2.Dot(delta, away) <= 0f ||
                        map.OccupiedTiles.Contains(Vector2Int.RoundToInt(candidate)) ||
                        !IsTerrainClear(position, candidate) || !HasWanderExitClearance(candidate))
                    {
                        continue;
                    }

                    float score = delta.sqrMagnitude;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        wanderExitTarget = candidate;
                    }
                }
            }
        }


        // 시체 이탈 구간 전체의 실제 유닛 간격 검사
        private bool HasWanderExitClearance(Vector2 destination)
        {
            // 도착점만 통과하고 중간에서 살아 있는 유닛에 막히는 경로 제외
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(position, destination) / MaximumSubstep));

            for (int i = 1; i <= samples; i++)
            {
                if (!HasUnitClearance(Vector2.Lerp(position, destination, i / (float)samples)))
                {
                    return false;
                }
            }

            return true;
        }


        // 배회 시작 구간을 실제 좌표에서 연속 이동
        private void UpdateWanderExit()
        {
            // 공격 대기 없이 유형별 평상시 이동 속도로 근접 구간 이탈
            Vector2 destination = wanderExitTarget.Value;
            Vector2 direction = destination - position;
            float baseSpeed = isZombie ? zombieSpeed : humanSpeed * (hasBeenHit ? InjuredSpeedMultiplier : 1f);
            float remaining = baseSpeed * speedVariation * UnitPathfinder.SpeedMultiplier(map, currentTile) * Time.deltaTime;
            float travelled = 0f;

            while (remaining > 0f && position != destination)
            {
                float step = Mathf.Min(remaining, MaximumSubstep, Vector2.Distance(position, destination));
                Vector2 next = Vector2.MoveTowards(position, destination, step);

                if (!CanPursueStep(next))
                {
                    wanderExitTarget = null;
                    break;
                }

                map.ReleaseTile(currentTile);
                position = next;
                currentTile = Vector2Int.RoundToInt(position);
                map.OccupyTile(currentTile);
                remaining -= step;
                travelled += step;
            }

            population[populationIndex] = currentTile;
            PublishPosition();
            transform.localPosition = new Vector3(position.x, position.y, 0f);

            if (travelled > 0f)
            {
                walkAnimation.Walk(direction, travelled);
            }
            else
            {
                walkAnimation.Stop();
            }

            if (position == destination)
            {
                wanderExitTarget = null;
                restRemaining = 0f;
            }
        }

        // 다른 개체와 겹치지 않는 가까운 위치에서 동일 개체를 좀비로 전환
        private void TryConvertToZombie()
        {
            // 공용 프레임이 없는 외부 생성 개체의 잘못된 변환 차단
            if (zombieFrames == null || zombieFrames.Length < 3)
            {
                return;
            }

            Vector2? destination = null;
            const int PlacementRadius = 4;
            float nearest = float.PositiveInfinity;

            for (int y = -PlacementRadius; y <= PlacementRadius; y++)
            {
                for (int x = -PlacementRadius; x <= PlacementRadius; x++)
                {
                    Vector2 candidate = position + new Vector2(x, y);
                    float distance = (candidate - position).sqrMagnitude;

                    if (distance >= nearest || !IsTerrainClear(position, candidate) || !HasConversionClearance(candidate))
                    {
                        continue;
                    }

                    destination = candidate;
                    nearest = distance;
                }
            }

            // 빈 공간이 없으면 쓰러진 상태에서 재시도하여 중복 생성 방지
            if (!destination.HasValue)
            {
                return;
            }

            int oldIndex = populationIndex;
            map.ConvertedHumans.Add(oldIndex);
            map.HumanUnits.Remove(oldIndex);
            map.HumanPositions.Remove(oldIndex);
            map.HumanSpeeds.Remove(oldIndex);
            map.HumanDestinations.Remove(oldIndex);

            // 쓰러질 때 반환한 점유를 재해제하지 않고 변환 위치에서 새 점유 확보
            position = destination.Value;
            currentTile = Vector2Int.RoundToInt(position);
            map.OccupyTile(currentTile);
            population = map.Zombies;
            populationIndex = population.Count;
            population.Add(currentTile);
            map.ZombieUnits[populationIndex] = this;
            isZombie = true;
            isDowned = false;
            hasBeenHit = false;
            hitCount = 0;
            chaseTarget = -1;
            riseRemaining = RiseDuration;
            walkAnimation.ChangeToZombie(zombieFrames);
            combat.BecomeZombie();
            PublishPosition();
            transform.localPosition = new Vector3(position.x, position.y, 0f);
            gameObject.name = "Converted Zombie";
            restRemaining = 0f;
            detectionRemaining = MinimumRest;
            map.NotifyPopulationChanged();
        }


        // 변환 위치에서 살아 있는 개체와 최소 간격 검사
        private bool HasConversionClearance(Vector2 candidate)
        {
            // 쓰러질 때 제거된 충돌 좌표를 제외한 활동 개체의 간격 확인
            foreach (var human in map.HumanPositions)
            {
                if (human.Key != populationIndex && Vector2.Distance(candidate, human.Value) < SeparationDistance)
                {
                    return false;
                }
            }

            foreach (var zombie in map.ZombiePositions.Values)
            {
                if (Vector2.Distance(candidate, zombie) < SeparationDistance)
                {
                    return false;
                }
            }

            return true;
        }

        // 포물선 피격 도약과 장애물을 관통하지 않는 지면 넉백
        private void UpdateKnockback()
        {
            // 피격 시작점에서 계산한 절대 목표 거리로 프레임별 누적 오차 방지
            knockbackElapsed = Mathf.Min(KnockbackDuration, knockbackElapsed + Time.deltaTime);
            float progress = knockbackElapsed / KnockbackDuration;
            float desiredDistance = EvaluateKnockbackDistance(progress);
            float travelled = KnockbackDistance - knockbackRemaining;
            walkAnimation.SetKnockbackHeight(4f * KnockbackHeight * progress * (1f - progress));

            while (desiredDistance - travelled > .00001f)
            {
                float step = Mathf.Min(desiredDistance - travelled, MaximumSubstep);
                Vector2 next = knockbackOrigin + knockbackDirection * (travelled + step);

                if (!CanPursueStep(next))
                {
                    // 한 번 막혀도 남은 거리를 버리지 않고 충돌 경계까지 안전하게 접근
                    const int BoundaryIterations = 10;
                    float low = 0f;
                    float high = step;

                    for (int i = 0; i < BoundaryIterations; i++)
                    {
                        float middle = (low + high) * .5f;
                        Vector2 candidate = knockbackOrigin + knockbackDirection * (travelled + middle);

                        if (CanPursueStep(candidate))
                        {
                            low = middle;
                        }
                        else
                        {
                            high = middle;
                        }
                    }

                    step = low;
                    next = knockbackOrigin + knockbackDirection * (travelled + step);
                }

                if (step <= .00001f)
                {
                    // 일시적인 유닛 방해는 다음 프레임에 남은 목표 거리로 재시도
                    break;
                }

                map.ReleaseTile(currentTile);
                currentTile = Vector2Int.RoundToInt(next);
                map.OccupyTile(currentTile);
                position = next;
                travelled += step;
                knockbackRemaining = Mathf.Max(0f, KnockbackDistance - travelled);
            }
            if (knockbackElapsed >= KnockbackDuration)
            {
                knockbackFinishedFrame = Time.frameCount;
            }

            population[populationIndex] = currentTile;
            PublishPosition();
            transform.localPosition = new Vector3(position.x, position.y, 0f);
            detectionRemaining = 0f;
            restRemaining = 0f;
        }

        // 피격 시작점 기준 감속 넉백 목표 거리 계산
        internal static float EvaluateKnockbackDistance(float progress)
        {
            // 착지 시 설정 거리 전체를 목표로 지정
            float remaining = 1f - Mathf.Clamp01(progress);
            return KnockbackDistance * (1f - remaining * remaining);
        }


        // 지형과 개체 편차 반영 후 인간보다 빠른 추적 속도 계산
        internal static float CalculatePursuitSpeed(float configuredSpeed, float humanActualSpeed)
        {
            // 설정 속도와 인간 실제 속도의 우위 보정 중 큰 값 선택
            return Mathf.Max(configuredSpeed, humanActualSpeed * PursuitSpeedAdvantage);
        }


        // 지정 좀비의 실제 위치 변화에서 가로채기 예측용 속도 갱신
        private void UpdateCooperativeTargetMotion()
        {
            // 같은 프레임의 중복 표본 제외 및 긴 관측 공백의 속도 폐기
            float elapsed = Time.time - cooperativeMotionSampleTime;

            if (elapsed <= 0f)
            {
                return;
            }

            cooperativeZombieVelocity = elapsed <= MaximumMotionSampleInterval
                ? (cooperativeZombie.position - cooperativePreviousZombiePosition) / elapsed : Vector2.zero;
            cooperativePreviousZombiePosition = cooperativeZombie.position;
            cooperativeMotionSampleTime = Time.time;
        }


        // 지원자 속도와 좀비 이동 방향을 사용한 공격 사거리 도달 예상 지점 조회
        private Vector2 FindCooperativeInterceptPosition(float speed)
        {
            // 근접 공격 중과 정지 및 넉백 상태에서는 현재 위치 추적
            Vector2 target = cooperativeZombie.position;
            Vector2 relative = target - position;

            if (speed <= 0f || relative.sqrMagnitude <= UnitCombat.HumanAttackRange * UnitCombat.HumanAttackRange ||
                combat.CanMoveDuringAttack || cooperativeZombie.combat.BlocksMovement ||
                cooperativeZombie.knockbackElapsed < KnockbackDuration || cooperativeZombieVelocity.sqrMagnitude <= InterceptCalculationTolerance)
            {
                return target;
            }

            // 좀비 예상 위치까지의 거리와 지원자 이동 거리 및 공격 사거리의 교차 시간 계산
            float attackRange = UnitCombat.HumanAttackRange;
            float a = cooperativeZombieVelocity.sqrMagnitude - speed * speed;
            float b = 2f * (Vector2.Dot(relative, cooperativeZombieVelocity) - attackRange * speed);
            float c = relative.sqrMagnitude - attackRange * attackRange;
            float interceptTime = MaximumInterceptTime;

            if (Mathf.Abs(a) <= InterceptCalculationTolerance)
            {
                if (b < -InterceptCalculationTolerance)
                {
                    interceptTime = -c / b;
                }
            }
            else
            {
                float discriminant = b * b - 4f * a * c;

                if (discriminant >= 0f)
                {
                    float root = Mathf.Sqrt(discriminant);
                    float first = (-b - root) / (2f * a);
                    float second = (-b + root) / (2f * a);

                    if (first >= 0f)
                    {
                        interceptTime = first;
                    }

                    if (second >= 0f)
                    {
                        interceptTime = first >= 0f ? Mathf.Min(first, second) : second;
                    }
                }
            }

            // 원형 도주의 방향 전환을 고려한 예측 시간 제한 및 지형 관통 예측 폐기
            Vector2 predicted = target + cooperativeZombieVelocity * Mathf.Clamp(interceptTime, 0f, MaximumInterceptTime);
            return cooperativeZombie.IsTerrainClear(target, predicted) && IsTerrainClear(position, predicted) ? predicted : target;
        }


        // 좀비의 인간 추격과 지원자의 예상 경로 가로채기를 위한 연속 이동
        private void PursueTarget()
        {
            // 기존 타일 구간 예약 반환 후 연속 위치의 점유로 전환
            if (!isDirectPursuit)
            {
                ReleaseReservations();
                map.ReleaseTile(currentTile);
                currentTile = Vector2Int.RoundToInt(position);
                map.OccupyTile(currentTile);
                isDirectPursuit = true;
                path.Clear();
                hasChasePath = false;
            }

            bool hasTarget = isZombie ? chaseTarget >= 0 : cooperativeZombie != null;

            // 지원자는 공격 시작 프레임에도 준비 중 추적 이동 유지 및 일반 공격은 정지
            if (hasTarget && (isZombie ? combat.TryAttack(chaseTarget, position) : combat.TryAttackZombie(position, cooperativeZombie)) &&
                combat.BlocksMovement)
            {
                return;
            }

            Vector2 target = !isZombie ? cooperativeZombie.Position : hasTarget && map.HumanPositions.TryGetValue(chaseTarget, out Vector2 live)
                ? live : hasTarget ? (Vector2)map.Humans[chaseTarget] : currentTile;
            float configuredSpeed = isZombie ? (hasTarget ? zombieChaseSpeed : zombieSpeed) : humanSpeed * (hasBeenHit ? InjuredSpeedMultiplier : 1f);
            float baseSpeed = configuredSpeed * speedVariation * UnitPathfinder.SpeedMultiplier(map, currentTile);

            if (isZombie && hasTarget && map.HumanSpeeds.TryGetValue(chaseTarget, out float humanActualSpeed))
            {
                baseSpeed = CalculatePursuitSpeed(baseSpeed, humanActualSpeed);
            }

            // 지원자는 현재 좀비 뒤를 따라가는 대신 예상 이동 경로로 측면 접근
            if (!isZombie && hasTarget)
            {
                target = FindCooperativeInterceptPosition(baseSpeed);

                // 세 명의 공격자는 넓게 대기하고 자기 공격 차례에만 근접 접근
                if (cooperativeLeader != null && cooperativeLeader.cooperativeSecondHelper != null)
                {
                    UnitWander leader = cooperativeLeader;
                    bool isRescuing = leader.cooperativeRescueTarget != null && leader.cooperativeRescuer == this;
                    bool isApproaching = isRescuing || (leader.cooperativeRescueTarget == null && Time.time >= cooperativeRetreatUntil &&
                        (combat.CanMoveDuringAttack || (combat.IsHumanAttackReady && HasCooperativeStrikeTurn(CooperativeApproachLeadTime))));
                    float flankDistance = isApproaching ? SeparationDistance : CooperativeStagingRadius;
                    Vector2 flank = CalculateCooperativeFlankPosition(flankDistance);
                    Vector2 interceptFlank = target + flank - cooperativeZombie.position;

                    // 구출자는 근접 측면 가로채기로 즉시 접근하고 다른 지원자는 넓은 측면 유지
                    if (IsTerrainClear(position, interceptFlank))
                    {
                        target = interceptFlank;
                    }
                }
            }

            // 직선이 물이나 건물에 막힐 때만 경로 탐색으로 보행 가능한 중간 지점 선택
            Vector2 waypoint = target;

            if (hasTarget && !IsTerrainClear(position, target))
            {
                UnitPathfinder.FindTargetPath(map, currentTile, Vector2Int.RoundToInt(target), path);

                if (path.Count == 0)
                {
                    walkAnimation.Stop(isZombie);
                    return;
                }

                waypoint = path[0];
            }

            float remaining = baseSpeed * Time.deltaTime;
            float travelled = 0f;
            Vector2 direction = waypoint - position;

            while (remaining > 0f)
            {
                float distance = Vector2.Distance(position, waypoint);
                // 지원자의 공격 준비 중에는 기존 충돌 간격까지 접근해 움직이는 좀비 추적
                float attackRange = isZombie || combat.CanMoveDuringAttack ? UnitCombat.GrabRange : UnitCombat.HumanAttackRange;

                // 예측 지점은 실제 좀비와 구분해 도착 전 정지 방지 및 공격은 실제 거리로만 판정
                if (!isZombie && target != cooperativeZombie.position)
                {
                    attackRange = 0f;
                }

                float available = hasTarget && waypoint == target ? Mathf.Max(0f, distance - attackRange) : distance;
                float step = Mathf.Min(remaining, MaximumSubstep, available);

                if (step <= 0f)
                {
                    break;
                }

                Vector2 next = Vector2.MoveTowards(position, waypoint, step);
                Vector2Int tile = Vector2Int.RoundToInt(next);

                // 앞을 가로막은 유닛은 가까운 측면 후보로 회피하고 겹침 방지 유지
                if (!CanPursueStep(next))
                {
                    bool canAvoid = false;
                    const float AvoidanceAngle = 45f;
                    Vector2 forward = (waypoint - position).normalized;

                    for (int attempt = 1; attempt <= 4; attempt++)
                    {
                        float angle = (attempt % 2 == 1 ? 1f : -1f) * ((attempt + 1) / 2) * AvoidanceAngle * Mathf.Deg2Rad;
                        Vector2 side = new Vector2(forward.x * Mathf.Cos(angle) - forward.y * Mathf.Sin(angle),
                            forward.x * Mathf.Sin(angle) + forward.y * Mathf.Cos(angle));
                        Vector2 candidate = position + side * step;

                        if (CanPursueStep(candidate))
                        {
                            next = candidate;
                            canAvoid = true;
                            break;
                        }
                    }

                    if (!canAvoid)
                    {
                        break;
                    }
                }

                tile = Vector2Int.RoundToInt(next);
                map.ReleaseTile(currentTile);
                currentTile = tile;
                map.OccupyTile(currentTile);
                direction = next - position;
                position = next;
                travelled += step;
                remaining -= step;
            }

            population[populationIndex] = currentTile;
            PublishPosition();
            transform.localPosition = new Vector3(position.x, position.y, 0f);

            if (travelled > 0f)
            {
                walkAnimation.Walk(direction, travelled);
            }
            else
            {
                walkAnimation.Stop(isZombie);
            }

            // 이동 직후 유형별 공격 거리 검사로 근접 공격 누락 방지
            if (hasTarget)
            {
                if (isZombie)
                {
                    combat.TryAttack(chaseTarget, position);
                }
                else
                {
                    combat.TryAttackZombie(position, cooperativeZombie);
                }
            }

            if (isZombie && chaseTarget < 0 && position == (Vector2)currentTile)
            {
                isDirectPursuit = false;
                path.Clear();
                pathIndex = 0;
                Rest();
            }
        }


        // 연속 추격의 지형 및 점유와 실제 유닛 간격 검사
        private bool CanPursueStep(Vector2 next)
        {
            // 이동 예약을 가상 장애물로 취급하지 않고 실제 유닛 간격으로 충돌 검사
            return IsTerrainClear(position, next) && HasUnitClearance(next);
        }


        // 이동 구간의 몸체 영역과 지형을 검사해 건물 및 모서리 관통 차단
        private bool IsTerrainClear(Vector2 from, Vector2 to)
        {
            // 건물 표시의 실제 경계와 스프라이트 불투명 영역으로 연속 충돌 검사
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / MaximumSubstep));
            Vector2Int previous = Vector2Int.RoundToInt(from);
            Vector2 previousPosition = from;
            Rect body = UnitWalkAnimation.BodyBounds;

            for (int i = 1; i <= samples; i++)
            {
                Vector2 next = Vector2.Lerp(from, to, i / (float)samples);
                Vector2Int tile = Vector2Int.RoundToInt(next);
                var sweptBody = Rect.MinMaxRect(Mathf.Min(previousPosition.x, next.x) + body.xMin,
                    Mathf.Min(previousPosition.y, next.y) + body.yMin,
                    Mathf.Max(previousPosition.x, next.x) + body.xMax,
                    Mathf.Max(previousPosition.y, next.y) + body.yMax);

                if (!UnitPathfinder.HasBuildingClearance(map, sweptBody) || !UnitPathfinder.CanOccupy(map, tile) ||
                    !UnitPathfinder.CanOccupy(map, new Vector2Int(previous.x, tile.y)) ||
                    !UnitPathfinder.CanOccupy(map, new Vector2Int(tile.x, previous.y)))
                {
                    return false;
                }

                previous = tile;
                previousPosition = next;
            }

            return true;
        }


        // 공격 거리와 동일한 최소 간격으로 살아 있는 유닛의 겹침 차단
        private bool HasUnitClearance(Vector2 next)
        {
            // 이미 근접한 상대와는 멀어지는 이동과 넉백을 허용
            foreach (var human in map.HumanPositions)
            {
                if (!isZombie && human.Key == populationIndex)
                {
                    continue;
                }

                if (!CanSeparateFrom(position, next, human.Value, SeparationDistance))
                {
                    return false;
                }
            }

            foreach (var zombie in map.ZombiePositions)
            {
                if ((!isZombie || zombie.Key != populationIndex) && !CanSeparateFrom(position, next, zombie.Value, SeparationDistance))
                {
                    return false;
                }
            }

            return true;
        }


        // 최소 간격 안에서는 더 가까워지는 이동만 차단
        internal static bool CanSeparateFrom(Vector2 from, Vector2 next, Vector2 other, float clearance)
        {
            // 붙잡힘 해제 후 짧은 넉백으로도 안전하게 도주 재개
            const float DistanceTolerance = .00001f;
            float nextDistance = (next - other).sqrMagnitude;
            return nextDistance + DistanceTolerance >= clearance * clearance ||
                nextDistance > (from - other).sqrMagnitude;
        }

        // 감지와 추적에서 참조할 실제 좌표 및 구간 목적지 공유
        private void PublishPosition()
        {
            // 정수 인구 목록과 별도로 연속 이동 좌표 전달
            var positions = isZombie ? map.ZombiePositions : map.HumanPositions;
            positions[populationIndex] = position;

            if (!isZombie && position == (Vector2)currentTile)
            {
                map.HumanDestinations[populationIndex] = currentTile;
            }
        }


        // 인접 이동의 도착 및 대각선 교차 영역 원자적 예약
        private bool TryReserve(Vector2Int target)
        {
            // 검사 단계에서 충돌 발견 시 예약 변경 없이 거부
            for (int y = Mathf.Min(currentTile.y, target.y); y <= Mathf.Max(currentTile.y, target.y); y++)
            {
                for (int x = Mathf.Min(currentTile.x, target.x); x <= Mathf.Max(currentTile.x, target.x); x++)
                {
                    var tile = new Vector2Int(x, y);

                    if (tile != currentTile && map.OccupiedTiles.Contains(tile))
                    {
                        return false;
                    }
                }
            }

            for (int y = Mathf.Min(currentTile.y, target.y); y <= Mathf.Max(currentTile.y, target.y); y++)
            {
                for (int x = Mathf.Min(currentTile.x, target.x); x <= Mathf.Max(currentTile.x, target.x); x++)
                {
                    var tile = new Vector2Int(x, y);

                    if (tile != currentTile)
                    {
                        map.OccupyTile(tile);
                        reservations.Add(tile);
                    }
                }
            }

            return true;
        }


        // 완료된 이동 구간의 임시 예약 반환
        private void ReleaseReservations()
        {
            // 해당 유닛이 소유한 예약만 해제
            foreach (var tile in reservations)
            {
                map.ReleaseTile(tile);
            }

            reservations.Clear();
        }


        // 참여 역할에 맞는 합동 공격 연결 정리
        private void ReleaseCooperativeAttack()
        {
            // 지원자도 도주자 소유 연결을 통해 함께 정리
            if (cooperativeLeader != null)
            {
                UnitWander leader = cooperativeLeader;

                // 세 명 중 지원자 이탈 시 남은 지원자로 기존 두 명의 협동 유지
                if (leader.cooperativeSecondHelper != null)
                {
                    leader.cooperativeHelper = leader.cooperativeHelper == this ? leader.cooperativeSecondHelper : leader.cooperativeHelper;
                    leader.cooperativeSecondHelper = null;
                    leader.cooperativeNextAttacker = null;
                    leader.cooperativeRescueTarget = null;
                    leader.cooperativeRescuer = null;
                    leader.DetachCooperativeHelper(this);
                    leader.ResetCooperativeMovement();
                }
                else
                {
                    leader.StopCooperativeAttack();
                }
            }
            else if (cooperativeHelper != null)
            {
                StopCooperativeAttack();
            }
        }


        // 비활성화 시 양방향 합동 공격 연결 해제
        private void OnDisable()
        {
            // 비활성 참여자를 다른 인간의 합동 공격에서 즉시 제외
            ReleaseCooperativeAttack();
        }


        // 유닛 파괴 시 점유 및 진행 중 예약 해제
        private void OnDestroy()
        {
            // 초기화된 지도에만 점유 반환
            if (map != null)
            {
                ReleaseReservations();

                // 시체는 쓰러질 때 점유를 반환하므로 다른 개체의 점유 해제 방지
                if (!isDowned)
                {
                    map.ReleaseTile(currentTile);
                }

                (isZombie ? map.ZombiePositions : map.HumanPositions).Remove(populationIndex);
                (isZombie ? map.ZombieUnits : map.HumanUnits).Remove(populationIndex);

                if (!isZombie)
                {
                    map.HumanDestinations.Remove(populationIndex);
                    map.HumanSpeeds.Remove(populationIndex);
                    map.HumanUnits.Remove(populationIndex);
                }
            }
        }


        // 도착 및 경로 탐색 실패 후 무작위 대기와 정지 포즈 적용
        private void Rest()
        {
            // 개체 전용 난수에서 다음 목적지 선택까지의 휴식 시간 결정
            restRemaining = Mathf.Lerp(MinimumRest, MaximumRest, (float)random.NextDouble());
            walkAnimation.Stop();
        }
    }
}
