using System.Collections.Generic;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 주변 배회와 좀비의 인간 추적 및 인간의 좀비 회피 이동 제어
    /// </summary>
    public sealed class UnitWander : MonoBehaviour
    {
        // 유형별 기본 능력치와 개체 속도 편차
        [SerializeField] private float humanSpeed = 1.8f;
        [SerializeField] private float zombieSpeed = 1.2f;
        [SerializeField] private float zombieChaseSpeed = 2.0f;
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

        // 이동 중 출발 및 도착과 대각선 교차 영역 예약
        private bool isDirectPursuit;
        private Vector2? wanderExitTarget;
        private const float SeparationDistance = .9f;
        private const float PursuitSpeedAdvantage = 1.25f;
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

        // 두 번째 피격 후 쓰러짐과 감염 변환 상태
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

        // 기존 인구 데이터와 이동 및 걷기 표시 연결
        public void Initialize(CityMap map, bool zombie, int index, Sprite[] zombieFrames = null, Sprite[] conversionFrames = null)
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
            walkAnimation.SetUnitType(zombie);
            this.zombieFrames = zombieFrames;
            this.conversionFrames = conversionFrames;
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

            // 쓰러진 인간의 이동을 차단하고 설정 시간 후 변환
            if (isDowned)
            {
                downedElapsed += Time.deltaTime;
                walkAnimation.SetFallProgress(downedElapsed / FallDuration);
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

            // 공격 준비 중 붙잡힌 인간은 도주와 걷기 모션 중지
            if (IsGrabbed)
            {
                walkAnimation.Stop();
                return;
            }

            if (knockbackElapsed < KnockbackDuration)
            {
                UpdateKnockback();
                return;
            }

            if (combat.BlocksMovement)
            {
                walkAnimation.Stop();
                return;
            }

            // 추격 속도 보정에 사용할 인간의 실제 지형 속도 공유
            if (!isZombie)
            {
                map.HumanSpeeds[populationIndex] = humanSpeed * (hasBeenHit ? InjuredSpeedMultiplier : 1f) * speedVariation * UnitPathfinder.SpeedMultiplier(map, currentTile);
            }

            // 공격 종료 직후에는 쓰러진 인간에서 안전하게 떨어지는 배회 구간 우선 실행
            if (isZombie && wanderExitTarget.HasValue)
            {
                UpdateWanderExit();
                return;
            }

            UpdateReaction();

            // 추적 중에는 타일 도착을 기다리지 않고 실제 인간 좌표로 연속 이동
            if (isZombie && (chaseTarget >= 0 || isDirectPursuit))
            {
                PursueHuman();
                return;
            }

            if (pathIndex >= path.Count)
            {
                restRemaining -= Time.deltaTime;

                if (restRemaining <= 0f && chaseTarget < 0)
                {
                    UnitPathfinder.FindWanderPath(map, currentTile, random, path);
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

                if (!HasUnitClearance(nextPosition))
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
                chaseTarget = isZombie ? UnitSight.FindChaseHuman(map, position, chaseTarget) : UnitSight.FindVisibleZombie(map, position);
            }

            // 연속 추격 중에는 감지만 갱신하고 타일 경로 교체 생략
            if (isZombie && (isDirectPursuit || chaseTarget >= 0))
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

            if (!hasChasePath || destination != chaseDestination || (pathIndex >= path.Count && pathRetryRemaining <= 0f))
            {
                // 일시적으로 막힌 경로도 주기적으로 재탐색해 영구 대기 방지
                pathRetryRemaining = DetectionInterval;

                // 좀비는 인간 위치 추적 및 인간은 좀비 반대 방향으로 도주
                if (isZombie)
                {
                    UnitPathfinder.FindTargetPath(map, currentTile, destination, path, true);
                }
                else
                {
                    UnitPathfinder.FindFleePath(map, currentTile, destination, path);
                }

                pathIndex = 0;
                chaseDestination = destination;
                hasChasePath = true;

                if (path.Count == 0)
                {
                    walkAnimation.Stop();
                }
            }
        }


        // 공격 준비 동안 인간 이동을 금지하는 붙잡기 시작
        public bool TryGrab(UnitCombat owner)
        {
            // 기존 소유자와 넉백 중 재포획 및 비활성 대상 거부
            if (!IsHumanTarget || !isActiveAndEnabled || owner == null || IsGrabbed || knockbackElapsed < KnockbackDuration)
            {
                return false;
            }

            grabOwner = owner;
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

        // 인간 피격의 최초 감속과 지정 거리 넉백 시작
        public void ReceiveHit(Vector2 direction)
        {
            // 좀비에 대한 피격 요청 무시 및 중복 감속 방지
            if (!IsHumanTarget || map == null)
            {
                return;
            }

            hitCount++;

            if (hitCount >= 2)
            {
                FallDown();
                return;
            }

            hasBeenHit = true;
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


        // 두 번째 피격 후 이동과 추가 타격 중단 및 쓰러짐 시작
        private void FallDown()
        {
            // 개체 점유는 유지하고 진행 중 이동 예약만 반환
            isDowned = true;
            downedElapsed = 0f;
            grabOwner = null;
            knockbackElapsed = KnockbackDuration;
            knockbackFinishedFrame = Time.frameCount;
            ReleaseReservations();
            path.Clear();
            pathIndex = 0;
            hasChasePath = false;
            map.InactiveHumans.Add(populationIndex);
            map.HumanSpeeds[populationIndex] = 0f;
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


        // 쓰러진 인간 가까이에서 충돌 없이 빠져나갈 배회 시작 구간 선택
        private void BeginWanderExit(Vector2 fallenPosition)
        {
            // 일반 타일 경로가 근접 시체에 막히는 구간은 실제 좌표에서 이탈
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

                    if (delta.sqrMagnitude <= .0001f || Vector2.Dot(delta, away) <= 0f ||
                        !IsTerrainClear(position, candidate) || !HasUnitClearance(candidate))
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


        // 배회 시작 구간을 실제 좌표에서 연속 이동
        private void UpdateWanderExit()
        {
            // 공격 대기 없이 평상시 좀비 속도로 근접 구간 이탈
            Vector2 destination = wanderExitTarget.Value;
            Vector2 direction = destination - position;
            float remaining = zombieSpeed * speedVariation * UnitPathfinder.SpeedMultiplier(map, currentTile) * Time.deltaTime;
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
            map.ReleaseTile(currentTile);
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


        // 변환 위치에서 모든 실제 개체와 최소 간격 검사
        private bool HasConversionClearance(Vector2 candidate)
        {
            // 자신의 인간 기록만 제외하고 쓰러진 다른 인간도 충돌 대상으로 유지
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


        // 실제 인간 좌표를 향한 연속 추격
        private void PursueHuman()
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

            bool hasTarget = chaseTarget >= 0;

            if (hasTarget && combat.TryAttack(chaseTarget, position))
            {
                return;
            }
            Vector2 target = hasTarget && map.HumanPositions.TryGetValue(chaseTarget, out Vector2 live)
                ? live : hasTarget ? (Vector2)map.Humans[chaseTarget] : currentTile;
            float baseSpeed = (hasTarget ? zombieChaseSpeed : zombieSpeed) * speedVariation * UnitPathfinder.SpeedMultiplier(map, currentTile);

            if (hasTarget && map.HumanSpeeds.TryGetValue(chaseTarget, out float humanActualSpeed))
            {
                baseSpeed = CalculatePursuitSpeed(baseSpeed, humanActualSpeed);
            }

            // 직선이 물이나 건물에 막힐 때만 경로 탐색으로 보행 가능한 중간 지점 선택
            Vector2 waypoint = target;

            if (hasTarget && !IsTerrainClear(position, target))
            {
                UnitPathfinder.FindTargetPath(map, currentTile, Vector2Int.RoundToInt(target), path);

                if (path.Count == 0)
                {
                    walkAnimation.Stop();
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
                float available = hasTarget && waypoint == target ? Mathf.Max(0f, distance - UnitCombat.GrabRange) : distance;
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
                    // 목표에 충분히 접근한 경우 회전하지 않고 최소 간격 유지
                    if (hasTarget && Vector2.Distance(position, target) <= UnitCombat.GrabRange + MaximumSubstep * 2f)
                    {
                        break;
                    }

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
                walkAnimation.Stop();
            }

            // 이동 직후 붙잡기 검사로 다음 프레임 인간 이동에 의한 공격 누락 방지
            if (hasTarget)
            {
                combat.TryAttack(chaseTarget, position);
            }

            if (chaseTarget < 0 && position == (Vector2)currentTile)
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


        // 선분을 작은 간격으로 검사해 물과 건물 및 모서리 관통 차단
        private bool IsTerrainClear(Vector2 from, Vector2 to)
        {
            // 논리 타일 중심 기준으로 연속 보행 구간 검사
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / MaximumSubstep));
            Vector2Int previous = Vector2Int.RoundToInt(from);

            for (int i = 1; i <= samples; i++)
            {
                Vector2Int tile = Vector2Int.RoundToInt(Vector2.Lerp(from, to, i / (float)samples));

                if (!UnitPathfinder.CanOccupy(map, tile) ||
                    !UnitPathfinder.CanOccupy(map, new Vector2Int(previous.x, tile.y)) ||
                    !UnitPathfinder.CanOccupy(map, new Vector2Int(tile.x, previous.y)))
                {
                    return false;
                }

                previous = tile;
            }

            return true;
        }


        // 추적 대상만 붙잡기 거리까지 접근 허용하고 나머지 유닛 간격 유지
        private bool HasUnitClearance(Vector2 next)
        {
            // 이미 근접한 상대와는 멀어지는 이동과 넉백을 허용
            foreach (var human in map.HumanPositions)
            {
                if (!isZombie && human.Key == populationIndex)
                {
                    continue;
                }

                float clearance = isZombie && human.Key == chaseTarget ? UnitCombat.GrabRange : SeparationDistance;

                if (!CanSeparateFrom(position, next, human.Value, clearance))
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


        // 유닛 파괴 시 점유 및 진행 중 예약 해제
        private void OnDestroy()
        {
            // 초기화된 지도에만 점유 반환
            if (map != null)
            {
                ReleaseReservations();
                map.ReleaseTile(currentTile);
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
