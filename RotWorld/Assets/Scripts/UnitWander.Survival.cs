using System.Collections.Generic;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 인간의 아사와 건물 출입·목표 이동·입구 유인 및 좀비의 은신 건물 인지 이동
    /// </summary>
    public sealed partial class UnitWander
    {
        // 아사 판정 한계와 아사자 전용 사망 표시
        private const double StarvationLimit = 12d * 60d * 60d;
        private UnitDeathVisuals starvationVisuals;
        private bool isStarved;

        // 인간 판단과 현재 건물 및 목표 입구 이동
        private HumanBrain brain;
        private CityBuilding insideBuilding;
        private CityBuilding navigationBuilding;
        private bool isHiding;
        private float survivalDecisionRemaining;
        private float navigationRetryRemaining;
        private const float NavigationRetryInterval = .5f;

        // 건물 내부 판단 주기와 퇴장 대기 상태
        private float insideDecisionRemaining;
        private double insideGameSeconds;
        private bool isLeavePending;

        // 입구 진입과 막힘 및 숨기 판정 거리
        private const float EntryDistance = .5f;
        private const float DoorGuardRadius = 3f;
        private const float DoorCheckDistance = 10f;
        private const float HideSearchRadius = 6f;
        internal const float BuildingViewRadius = 5f;

        // 입구를 막은 좀비 한 마리의 유인 단계
        private enum LurePhase
        {
            None,
            Bait,
            Lead,
            Circle,
            Dash
        }

        private LurePhase lurePhase;
        private UnitWander lureZombie;
        private int lureAttempts;
        private float lurePhaseStartTime;
        private const float LureLeadDistance = 8f;
        private const float LureDashMargin = 2f;
        private const int MaximumLureAttempts = 2;
        private const float LurePhaseTimeout = 12f;
        private readonly List<Vector2> lureThreats = new List<Vector2>(2);

        // 쫓던 인간이 숨은 건물의 입구 주변 배회
        private CityBuilding alertBuilding;
        private const int ZombieLingerRadius = 4;
        private const int LingerPickAttempts = 8;

        // 생존 판단의 주변 후보 재사용 목록
        private readonly List<int> survivalCandidates = new List<int>();
        private readonly List<CityBuilding> nearbyBuildings = new List<CityBuilding>();


        // 입구 유인과 도주 중 숨기 및 생존 목표 이동의 경로 담당 여부 갱신
        private bool UpdateSurvivalPath()
        {
            // 판단 객체가 없는 개체와 타일 경계가 아닌 시점은 기존 흐름 유지
            if (brain == null)
            {
                return false;
            }

            bool shouldDecide = survivalDecisionRemaining <= 0f;

            if (shouldDecide)
            {
                survivalDecisionRemaining = DetectionInterval;
                brain.EatFromInventory();
                ValidateLure();
            }

            // 입구 유인은 일반 도주보다 우선하고 원형 이동 구간은 Update에서 처리
            if (lurePhase != LurePhase.None)
            {
                if (lurePhase != LurePhase.Circle && (shouldDecide || pathIndex >= path.Count))
                {
                    PlanLurePath();
                }

                return true;
            }

            // 쫓기는 중에는 숨을 건물이 있을 때만 경로를 맡고 나머지는 기존 도주 사용
            if (chaseTarget >= 0)
            {
                if (shouldDecide)
                {
                    UpdateHiding();
                }

                return isHiding;
            }

            // 위협이 사라지면 숨기 이동을 끝내고 원래 목표 재개
            if (isHiding)
            {
                CancelNavigation();
            }

            if (shouldDecide)
            {
                UpdateGoalNavigation();
            }

            return navigationBuilding != null;
        }


        // 휴식이 끝난 뒤 무작위 배회 대신 사용할 생존 경로 계획
        private bool TryPlanSurvivalPath()
        {
            // 좀비는 은신 건물 인지 배회, 인간은 목표 입구가 있으면 배회 생략
            if (isZombie)
            {
                return TryPlanAlertPath();
            }

            return brain != null && navigationBuilding != null;
        }


        // 판단 객체의 목표 건물로 입구 경로 계획과 막힌 입구 처리
        private void UpdateGoalNavigation()
        {
            // 도주가 끝난 뒤 남은 도주 경로 폐기
            if (hasChasePath)
            {
                path.Clear();
                pathIndex = 0;
                hasChasePath = false;
            }

            CityBuilding target = brain.SelectTarget(position);

            if (target != navigationBuilding)
            {
                navigationBuilding = target;
                lureAttempts = 0;
                path.Clear();
                pathIndex = 0;
                navigationRetryRemaining = 0f;
            }

            if (target == null || TryEnterNavigationBuilding())
            {
                return;
            }

            // 입구 근처에서 입구 3타일 안 좀비 수에 따라 유인 또는 목표 변경
            Vector2Int door = target.Door;

            if (((Vector2)door - position).sqrMagnitude <= DoorCheckDistance * DoorCheckDistance)
            {
                int guards = CountZombiesNear(door, DoorGuardRadius, out UnitWander guard);

                if (guards >= 2)
                {
                    brain.MarkDanger(target);
                    CancelNavigation();
                    return;
                }

                if (guards == 1)
                {
                    StartLure(guard);
                    return;
                }
            }

            if (pathIndex >= path.Count && navigationRetryRemaining <= 0f)
            {
                PlanPathTo(door);
            }
        }


        // 지도 전체 시간 비용 경로로 지정 타일까지 이동 경로 계획
        private void PlanPathTo(Vector2Int goal)
        {
            // 경로가 없으면 짧은 간격 뒤 재시도
            UnitPathfinder.FindTargetPath(map, currentTile, goal, path);
            PreparePathStart();
            pathIndex = 0;
            hasChasePath = false;

            if (path.Count == 0)
            {
                navigationRetryRemaining = NavigationRetryInterval;
            }
        }


        // 목표 입구에 도착한 경우 진입 조건 확인 후 건물 진입
        private bool TryEnterNavigationBuilding()
        {
            // 입구 타일 중심 도달 전과 이미 건물 안인 경우 생략
            if (navigationBuilding == null || insideBuilding != null ||
                ((Vector2)navigationBuilding.Door - position).sqrMagnitude > EntryDistance * EntryDistance)
            {
                return false;
            }

            // 숨기와 유인 돌입은 즉시 진입하고 일반 목표는 건물 시야의 좀비 수로 판단
            bool isForced = isHiding || lurePhase == LurePhase.Dash;

            if (!isForced && !brain.ShouldEnter(navigationBuilding, CountZombiesInView(navigationBuilding, out _)))
            {
                CancelNavigation();
                return false;
            }

            EnterBuilding(navigationBuilding, isHiding);
            return true;
        }


        // 건물 진입 후 지도 감지·충돌·추격 대상에서 제외하고 표시 숨김
        private void EnterBuilding(CityBuilding building, bool hiding)
        {
            // 쫓던 좀비에게만 은신 건물 전달
            map.CollectPursuers(populationIndex, survivalCandidates);
            map.ZombieQueries.SortInOriginalOrder(survivalCandidates);

            foreach (int index in survivalCandidates)
            {
                if (map.ZombieUnits.TryGetValue(index, out UnitWander zombie) && zombie != null)
                {
                    zombie.LoseHiddenHuman(this, populationIndex, building);
                }
            }

            // 협동과 공격 및 생존 이동 상태 정리
            ReleaseCooperativeAttack();
            combat.CancelAttack();
            ClearSurvivalMovement();
            SetChaseTarget(-1);

            // 점유와 공유 좌표 및 검색 색인에서 제거해 좀비 감지와 충돌 대상 제외
            ReleaseReservations();
            map.ReleaseTile(currentTile);
            population[populationIndex] = currentTile;
            map.InactiveHumans.Add(populationIndex);
            map.RemoveUnitPosition(false, populationIndex);
            map.HumanSpatialIndex.Remove(populationIndex);
            map.HumanQueries.Remove(populationIndex);
            map.HumanDestinations.Remove(populationIndex);
            path.Clear();
            pathIndex = 0;
            hasChasePath = false;
            isDirectPursuit = false;
            needsFleePathUpdate = false;
            wanderExitTarget = null;

            // 내부 상태 전환과 인원 기록 및 판단 객체 통지
            insideBuilding = building;
            insideDecisionRemaining = 0f;
            insideGameSeconds = 0d;
            isLeavePending = false;
            transform.localPosition = new Vector3(position.x, position.y, 0f);
            walkAnimation.Stop();
            walkAnimation.SetVisible(false);
            map.Occupancy.Enter(building, this);
            brain.OnEntered(building, hiding);
        }


        // 건물 내부 체류 판단과 출입구가 안전할 때 퇴장
        private void UpdateInsideBuilding()
        {
            // 배속이 반영된 게임 시간을 판단 주기마다 전달
            insideGameSeconds += Time.deltaTime * GameClock.GameSecondsPerSecond;
            insideDecisionRemaining -= Time.deltaTime;

            if (insideDecisionRemaining > 0f)
            {
                return;
            }

            insideDecisionRemaining = DetectionInterval;

            // 퇴장 결정 후에는 출입구가 열릴 때까지 섭취만 유지
            if (isLeavePending)
            {
                brain.EatInside(insideBuilding);
            }
            else
            {
                int zombies = CountZombiesInView(insideBuilding, out Vector2 zombieCenter);
                isLeavePending = brain.UpdateInside(insideBuilding, insideGameSeconds, zombies, zombieCenter);
            }

            insideGameSeconds = 0d;

            if (isLeavePending && CanExitBuilding(insideBuilding))
            {
                ExitBuilding();
            }
        }


        // 출입구 3타일 안 좀비와 출입구 위 유닛 여부로 퇴장 가능 판단
        private bool CanExitBuilding(CityBuilding building)
        {
            // 이동 예약이나 좀비가 있는 출입구에는 등장하지 않음
            Vector2Int door = building.Door;

            if (map.OccupiedTiles.Contains(door) || CountZombiesNear(door, DoorGuardRadius, out _) > 0)
            {
                return false;
            }

            // 다른 인간과 겹치지 않는 출입구에서만 등장
            map.HumanSpatialIndex.Collect(door, SeparationDistance, survivalCandidates);

            foreach (int index in survivalCandidates)
            {
                if (map.HumanPositions.TryGetValue(index, out Vector2 other) &&
                    ((Vector2)door - other).sqrMagnitude < SeparationDistance * SeparationDistance)
                {
                    return false;
                }
            }

            return true;
        }


        // 출입구 타일에 다시 등록하고 표시 복원
        private void ExitBuilding()
        {
            // 인원 기록 해제 후 감지 대상 복귀
            CityBuilding building = insideBuilding;
            insideBuilding = null;
            isLeavePending = false;
            map.Occupancy.Exit(building, this);
            map.InactiveHumans.Remove(populationIndex);

            // 출입구 위치의 점유와 공유 좌표 및 검색 색인 등록
            currentTile = building.Door;
            SetPosition(currentTile);
            map.OccupyTile(currentTile);
            population[populationIndex] = currentTile;
            PublishPosition();
            transform.localPosition = new Vector3(position.x, position.y, 0f);
            walkAnimation.SetVisible(true);

            // 다음 프레임에 즉시 위협 감지와 목표 판단 수행
            path.Clear();
            pathIndex = 0;
            detectionRemaining = 0f;
            survivalDecisionRemaining = 0f;
            navigationRetryRemaining = 0f;
            restRemaining = 0f;
        }


        // 0 지속 시간 한계를 넘은 인간의 쓰러짐 또는 건물 안 소멸
        private void Starve()
        {
            // 건물 밖은 쓰러진 뒤 흰색 전환 후 먼지 소멸
            if (insideBuilding == null)
            {
                FallDown(true);
                return;
            }

            // 건물 안은 인원에서 빼고 표시 없이 즉시 제거하며 점유는 진입 시 반환 완료
            map.Occupancy.Exit(insideBuilding, this);
            insideBuilding = null;
            isDowned = true;
            isStarved = true;
            Needs.Freeze();
            brain.ReleaseSettlement();
            map.StarvedHumans.Add(populationIndex);
            map.NotifyPopulationChanged();
            enabled = false;
            Destroy(gameObject);
        }


        // 쫓기는 중 좀비 반대편의 안전한 입구 선택과 숨기 경로 유지
        private void UpdateHiding()
        {
            // 협동 공격 참여자는 기존 협동 이동 우선
            if (cooperativeHelper != null || cooperativeLeader != null)
            {
                if (isHiding)
                {
                    CancelNavigation();
                }

                return;
            }

            // 숨기 중 입구에 좀비가 생기면 포기하고 일반 도주 재계산
            if (isHiding)
            {
                if (navigationBuilding == null || CountZombiesNear(navigationBuilding.Door, DoorGuardRadius, out _) > 0)
                {
                    CancelNavigation();
                    hasChasePath = false;
                }
                else if (pathIndex >= path.Count && navigationRetryRemaining <= 0f && !TryEnterNavigationBuilding())
                {
                    PlanPathTo(navigationBuilding.Door);
                }

                return;
            }

            if (!map.ZombiePositions.TryGetValue(chaseTarget, out Vector2 threat))
            {
                return;
            }

            // 6타일 안 입구 중 좀비 방향과 90도 이상 벌어지고 입구 3타일 안 좀비가 없는 가장 가까운 건물
            map.Occupancy.CollectBuildingsNear(position, HideSearchRadius, nearbyBuildings);
            CityBuilding best = null;
            float bestDistance = float.PositiveInfinity;

            foreach (var building in nearbyBuildings)
            {
                Vector2 offset = (Vector2)building.Door - position;
                float distance = offset.sqrMagnitude;

                if (distance >= bestDistance || Vector2.Dot(offset, threat - position) > 0f ||
                    (building.Door != currentTile && map.OccupiedTiles.Contains(building.Door)) ||
                    CountZombiesNear(building.Door, DoorGuardRadius, out _) > 0)
                {
                    continue;
                }

                best = building;
                bestDistance = distance;
            }

            if (best == null)
            {
                return;
            }

            // 도주 경로를 입구 경로로 교체하고 경로가 없으면 일반 도주 유지
            navigationBuilding = best;
            isHiding = true;
            lureAttempts = 0;

            if (TryEnterNavigationBuilding())
            {
                return;
            }

            PlanPathTo(best.Door);

            if (path.Count == 0)
            {
                CancelNavigation();
            }
        }


        // 현재 목표 입구 이동과 숨기 상태 해제
        private void CancelNavigation()
        {
            // 타일 경계에서만 호출되어 경로 교체로 인한 관통 없음
            navigationBuilding = null;
            isHiding = false;
            path.Clear();
            pathIndex = 0;
        }


        // 쓰러짐과 진입 시 입구 유인과 목표 이동 상태 초기화
        private void ClearSurvivalMovement()
        {
            // 경로와 점유는 호출자가 정리
            lurePhase = LurePhase.None;
            lureZombie = null;
            isHiding = false;
            navigationBuilding = null;
        }


        // 입구를 막은 좀비 한 마리의 유인 시작과 실패 횟수 제한
        private void StartLure(UnitWander guard)
        {
            // 두 번 시도한 건물은 위험 표시 후 다른 건물 선택
            if (lureAttempts >= MaximumLureAttempts)
            {
                brain.MarkDanger(navigationBuilding);
                CancelNavigation();
                return;
            }

            lureAttempts++;
            lureZombie = guard;
            SetLurePhase(LurePhase.Bait);
            PlanLurePath();
        }


        // 유인 단계 전환 시각 기록
        private void SetLurePhase(LurePhase phase)
        {
            // 단계별 제한 시간 계산 기준 갱신
            lurePhase = phase;
            lurePhaseStartTime = Time.time;
        }


        // 유인 조건 유지 확인과 단계 전환
        private void ValidateLure()
        {
            // 진행 중인 유인이 없으면 생략
            if (lurePhase == LurePhase.None)
            {
                return;
            }

            // 대상 상실과 협동 참여 및 다른 좀비 접근 시 일반 도주로 전환
            if (navigationBuilding == null || lureZombie == null || !lureZombie.IsZombieTarget || !lureZombie.isActiveAndEnabled ||
                cooperativeHelper != null || cooperativeLeader != null || HasOtherZombieNear(position, UnitSight.FleeScanRadius, lureZombie))
            {
                AbortLure();
                return;
            }

            Vector2 door = navigationBuilding.Door;
            bool isChased = lureZombie.chaseTarget == populationIndex;
            bool isTimedOut = Time.time - lurePhaseStartTime > LurePhaseTimeout;

            switch (lurePhase)
            {
                case LurePhase.Bait:
                    // 좀비가 쫓기 시작하면 건물 반대쪽 일직선 유인
                    if (isChased)
                    {
                        SetLurePhase(LurePhase.Lead);
                        path.Clear();
                        pathIndex = 0;
                    }
                    else if (isTimedOut)
                    {
                        AbortLure();
                    }

                    break;
                case LurePhase.Lead:
                    // 좀비가 놓치면 바로 입구로, 충분히 멀어지면 원형 우회 시작
                    if (!isChased)
                    {
                        BeginLureDash();
                    }
                    else if ((position - door).sqrMagnitude >= LureLeadDistance * LureLeadDistance || isTimedOut)
                    {
                        BeginLureOrbit(door);
                    }

                    break;
                case LurePhase.Circle:
                    // 좀비보다 입구에 가까워지면 입구로 돌입
                    if (!isChased || HasPassedLureZombie(door))
                    {
                        BeginLureDash();
                    }
                    else if (isTimedOut)
                    {
                        AbortLure();
                    }

                    break;
                case LurePhase.Dash:
                    // 좀비가 다시 입구를 막거나 제한 시간을 넘기면 실패
                    if (isTimedOut || CountZombiesNear(door, SeparationDistance, out _) > 0)
                    {
                        AbortLure();
                    }

                    break;
            }
        }


        // 좀비 기준 원형 우회 시작과 입구 쪽 회전 방향 선택
        private void BeginLureOrbit(Vector2 door)
        {
            // 좀비에서 본 현재 위치와 입구 사이의 짧은 회전 방향 사용
            Vector2 radial = position - lureZombie.position;
            Vector2 toDoor = door - lureZombie.position;
            cooperativeOrbitDirection = radial.x * toDoor.y - radial.y * toDoor.x >= 0f ? 1f : -1f;
            isOrbitAvoidingObstacle = false;
            orbitAvoidanceTravelled = 0f;
            isDirectPursuit = false;
            path.Clear();
            pathIndex = 0;
            SetLurePhase(LurePhase.Circle);
        }


        // 원형 우회를 마치고 입구까지 경로 이동 시작
        private void BeginLureDash()
        {
            // 실제 좌표 이동에서 타일 경로 이동으로 복귀
            if (isDirectPursuit)
            {
                ResetCooperativeMovement();
            }

            SetLurePhase(LurePhase.Dash);
            PlanLurePath();
        }


        // 유인 중단 후 일반 도주와 목표 판단 복귀
        private void AbortLure()
        {
            // 원형 이동 중이면 타일 점유와 경로 상태 복원
            if (isDirectPursuit)
            {
                ResetCooperativeMovement();
            }

            lurePhase = LurePhase.None;
            lureZombie = null;
            path.Clear();
            pathIndex = 0;
            hasChasePath = false;
        }


        // 원형 우회 구간의 좀비 기준 실제 좌표 이동
        private void UpdateLureOrbit()
        {
            // 대상이나 목표를 잃으면 유인 중단
            if (lureZombie == null || !lureZombie.IsZombieTarget || navigationBuilding == null)
            {
                AbortLure();
                return;
            }

            MoveAlongOrbit(lureZombie.position, lureZombie.position);

            if (HasPassedLureZombie(navigationBuilding.Door))
            {
                BeginLureDash();
            }
        }


        // 유인 단계별 타일 경로 계획
        private void PlanLurePath()
        {
            // 접근은 좀비 인접 칸, 유인은 좀비와 입구 반대 방향, 돌입은 입구까지
            Vector2Int zombieTile = Vector2Int.RoundToInt(lureZombie.position);

            switch (lurePhase)
            {
                case LurePhase.Bait:
                    UnitPathfinder.FindTargetPath(map, currentTile, zombieTile, path, true);
                    break;
                case LurePhase.Lead:
                    lureThreats.Clear();
                    lureThreats.Add(lureZombie.position);
                    lureThreats.Add(navigationBuilding.Door);
                    UnitPathfinder.FindFleePath(map, currentTile, zombieTile, lureThreats, path);
                    break;
                case LurePhase.Dash:
                    UnitPathfinder.FindTargetPath(map, currentTile, navigationBuilding.Door, path);
                    break;
                default:
                    return;
            }

            PreparePathStart();
            pathIndex = 0;
            hasChasePath = false;
        }


        // 인간이 유인 좀비보다 입구에 충분히 가까워졌는지 조회
        private bool HasPassedLureZombie(Vector2 door)
        {
            // 좀비 반대편으로 돌아 입구 쪽에 선 경우만 돌입 허용
            return Vector2.Distance(position, door) + LureDashMargin < Vector2.Distance(lureZombie.position, door);
        }


        // 지정 반경 안의 활동 중인 좀비 수와 첫 좀비 조회
        private int CountZombiesNear(Vector2 center, float radius, out UnitWander first)
        {
            // 공간 색인 후보에 원형 거리 판정 적용
            map.ZombieSpatialIndex.Collect(center, radius + ActionQueryMargin, survivalCandidates);
            int count = 0;
            first = null;

            foreach (int index in survivalCandidates)
            {
                if (!map.ZombiePositions.TryGetValue(index, out Vector2 zombiePosition) ||
                    (zombiePosition - center).sqrMagnitude > radius * radius ||
                    !map.ZombieUnits.TryGetValue(index, out UnitWander zombie) || zombie == null || !zombie.IsZombieTarget)
                {
                    continue;
                }

                count++;

                if (first == null)
                {
                    first = zombie;
                }
            }

            return count;
        }


        // 지정 좀비를 제외한 다른 활동 좀비의 근접 여부 조회
        private bool HasOtherZombieNear(Vector2 center, float radius, UnitWander except)
        {
            // 유인 대상 외 위협이 생기면 유인 중단에 사용
            map.ZombieSpatialIndex.Collect(center, radius + ActionQueryMargin, survivalCandidates);

            foreach (int index in survivalCandidates)
            {
                if (map.ZombiePositions.TryGetValue(index, out Vector2 zombiePosition) &&
                    (zombiePosition - center).sqrMagnitude <= radius * radius &&
                    map.ZombieUnits.TryGetValue(index, out UnitWander zombie) && zombie != null && zombie != except && zombie.IsZombieTarget)
                {
                    return true;
                }
            }

            return false;
        }


        // 건물 영역에서 5타일 안의 활동 좀비 수와 평균 위치 조회
        private int CountZombiesInView(CityBuilding building, out Vector2 zombieCenter)
        {
            // 건물 영역 외곽까지의 거리로 판정해 건물 크기 반영
            RectInt bounds = building.Bounds;
            Vector2 center = bounds.center;
            float radius = BuildingViewRadius + .5f * Mathf.Sqrt(bounds.width * bounds.width + bounds.height * bounds.height) + 1f;
            map.ZombieSpatialIndex.Collect(center, radius, survivalCandidates);
            Vector2 sum = Vector2.zero;
            int count = 0;

            foreach (int index in survivalCandidates)
            {
                if (!map.ZombiePositions.TryGetValue(index, out Vector2 zombiePosition) ||
                    !map.ZombieUnits.TryGetValue(index, out UnitWander zombie) || zombie == null || !zombie.IsZombieTarget)
                {
                    continue;
                }

                // 좀비 표시 좌표의 타일 중심과 건물 영역 사이 최단 거리
                Vector2 point = zombiePosition + Vector2.one * .5f;
                float dx = Mathf.Max(0f, Mathf.Max(bounds.xMin - point.x, point.x - bounds.xMax));
                float dy = Mathf.Max(0f, Mathf.Max(bounds.yMin - point.y, point.y - bounds.yMax));

                if (dx * dx + dy * dy > BuildingViewRadius * BuildingViewRadius)
                {
                    continue;
                }

                sum += zombiePosition;
                count++;
            }

            zombieCenter = count > 0 ? sum / count : center;
            return count;
        }


        // 쫓던 인간이 건물에 숨은 경우 추격 해제와 은신 건물 기억
        private void LoseHiddenHuman(UnitWander human, int humanIndex, CityBuilding building)
        {
            // 다른 인간을 쫓는 좀비 상태 보존
            if (chaseTarget != humanIndex)
            {
                return;
            }

            // 추격과 공격을 멈추고 다음 휴식 종료 시 입구로 이동
            combat.CancelHumanTarget(human);
            ReleaseReservations();
            SetChaseTarget(-1);
            hasChasePath = false;
            isDirectPursuit = false;
            path.Clear();
            pathIndex = 0;
            restRemaining = 0f;
            alertBuilding = building;
            walkAnimation.Stop();
        }


        // 인지한 은신 건물의 입구 또는 입구 4타일 안 빈 타일로 이동 계획
        private bool TryPlanAlertPath()
        {
            // 인지 건물이 없거나 비면 기존 배회 복귀
            if (alertBuilding == null)
            {
                return false;
            }

            if (map.Occupancy.GetCount(alertBuilding) == 0)
            {
                alertBuilding = null;
                return false;
            }

            // 입구에서 멀면 입구로, 가까우면 입구 주변 빈 타일 중 무작위 선택
            Vector2Int door = alertBuilding.Door;
            Vector2Int goal = door;

            if ((currentTile - door).sqrMagnitude <= ZombieLingerRadius * ZombieLingerRadius)
            {
                goal = currentTile;

                for (int attempt = 0; attempt < LingerPickAttempts; attempt++)
                {
                    var candidate = door + new Vector2Int(random.Next(-ZombieLingerRadius, ZombieLingerRadius + 1),
                        random.Next(-ZombieLingerRadius, ZombieLingerRadius + 1));

                    if (candidate != currentTile && (candidate - door).sqrMagnitude <= ZombieLingerRadius * ZombieLingerRadius &&
                        UnitPathfinder.CanOccupy(map, candidate) && !map.OccupiedTiles.Contains(candidate))
                    {
                        goal = candidate;
                        break;
                    }
                }
            }

            // 이동할 칸이 없으면 제자리에서 다시 휴식
            if (goal == currentTile)
            {
                Rest();
                return true;
            }

            UnitPathfinder.FindTargetPath(map, currentTile, goal, path);
            PreparePathStart();
            pathIndex = 0;

            if (path.Count == 0)
            {
                Rest();
            }

            return true;
        }
    }
}
