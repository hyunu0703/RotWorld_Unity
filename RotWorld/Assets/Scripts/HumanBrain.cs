using System;
using System.Collections.Generic;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 인간의 정착·파밍·비축·귀환·이주 목표 판단과 건물 내부 체류 결정
    /// </summary>
    internal sealed class HumanBrain
    {
        // 판단 목표 종류
        internal enum Goal
        {
            None,
            Shelter,
            Forage,
            Home
        }

        // 파밍 탐색 반경과 건물별 체류 시간 및 위험 기억 시간
        private const float ForageSearchRadius = 20f;
        private const int ForageRadiusSteps = 4;
        private const double ForageStaySeconds = 30d * 60d;
        private const double SearchSeconds = ForageStaySeconds;
        private const double HideStaySeconds = 30d * 60d;
        private const int AbandonZombieCount = 3;
        private const float DangerMemoryDuration = 120f;

        // 소유 지도와 섭취·파밍 규칙
        private readonly CityMap map;
        private readonly HumanForaging foraging;

        // 현재 정착지와 이동 목표 및 파밍 진행 상태
        internal CityBuilding Settlement { get; private set; }
        internal CityBuilding Target { get; private set; }
        internal Goal CurrentGoal { get; private set; }
        private bool isForaging;

        // 한 번의 파밍 동안 방문한 건물과 후보 소진 후 정착지 대기 여부
        private readonly HashSet<CityBuilding> tripVisited = new HashSet<CityBuilding>();
        private bool isForageExhausted;

        // 위험 표시 건물과 이주 시 피할 좀비 방향
        private readonly Dictionary<CityBuilding, float> dangerUntil = new Dictionary<CityBuilding, float>();
        private Vector2? abandonedThreat;

        // 후보가 없을 때 전체 건물 재검색 간격
        private const float SearchRetryInterval = 2f;
        private float nextSearchTime;

        // 현재 건물 체류 시간과 숨기 진입 여부
        private double stayElapsed;
        private bool isHiding;

        // 소유 인간의 지도와 생존 상태 연결
        internal HumanBrain(CityMap map, HumanNeeds needs, HumanInventory inventory)
        {
            // 판단에 필요한 지도와 개인 상태 누락 차단
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            foraging = new HumanForaging(needs, inventory);
        }


        // 건물 밖에서 비상 기준 이하일 때 소지품 섭취
        internal void EatFromInventory()
        {
            // 소지품은 비상용이므로 탐색 기준 이하이면 섭취와 무관하게 파밍 진행 상태로 전환
            foraging.EatFromInventory();
            RefreshForaging();
        }


        // 퇴장 대기 중 현재 건물 재고와 비상 소지품 순으로 섭취
        internal void EatInside(CityBuilding building)
        {
            // 출입구가 막혀 기다리는 동안에도 아사 방지 섭취 유지
            EatInBuilding(building);
        }


        // 정착지 해제와 정착 인원 반환
        internal void ReleaseSettlement()
        {
            // 쓰러짐·변환·제거 시 남은 정착 인원 기록 정리
            SetSettlement(null);
        }


        // 건물 밖에서 향할 건물 선택
        internal CityBuilding SelectTarget(Vector2 position)
        {
            // 파밍과 귀환 및 정착지 탐색 순으로 목표 결정
            Goal goal = isForaging ? Goal.Forage : Settlement != null ? Goal.Home : Goal.Shelter;

            if (goal != CurrentGoal || (Target != null && IsDangerous(Target)))
            {
                CurrentGoal = goal;
                Target = null;
            }

            // 직전 검색이 실패했으면 재검색 간격 동안 전체 건물 순회 생략
            if (Target != null || Time.time < nextSearchTime)
            {
                return Target;
            }

            switch (goal)
            {
                case Goal.Forage:
                    // 탐색 기준 이하이고 정착지에 쓸 재고가 남아 있으면 이번 파밍에서 한 번은 정착지부터 확인
                    Target = ShouldEatAtSettlement() ? Settlement : FindForageTarget(position);

                    // 후보가 없으면 방문 기록을 비우고 정착지 또는 새 주거 시설에서 대기 후 재탐색
                    if (Target == null)
                    {
                        tripVisited.Clear();
                        isForageExhausted = true;
                        Target = Settlement != null ? Settlement : FindShelter(position);
                    }

                    break;
                case Goal.Home:
                    Target = Settlement;
                    break;
                default:
                    Target = FindShelter(position);
                    break;
            }

            if (Target == null)
            {
                nextSearchTime = Time.time + SearchRetryInterval;
            }

            return Target;
        }


        // 일정 시간 동안 접근하지 않을 위험 건물 표시
        internal void MarkDanger(CityBuilding building)
        {
            // 위험 정착지는 버리고 현재 목표에서도 제외
            if (building == null)
            {
                return;
            }

            dangerUntil[building] = Time.time + DangerMemoryDuration;

            // 버린 정착지의 재고는 그대로 두고 정착 인원만 반환
            if (Settlement == building)
            {
                SetSettlement(null);
            }

            if (Target == building)
            {
                Target = null;
            }
        }


        // 입구 도착 시 건물 시야의 좀비 수로 진입 여부 결정
        internal bool ShouldEnter(CityBuilding building, int zombiesInView)
        {
            // 새 정착 후보는 좀비가 보이면 위험 표시하고 정착지는 이주 기준 이상일 때만 포기
            if (building == Settlement ? zombiesInView >= AbandonZombieCount : CurrentGoal == Goal.Shelter && zombiesInView > 0)
            {
                MarkDanger(building);
                return false;
            }

            return true;
        }


        // 건물 진입 시 정착지 확정과 체류 시간 초기화
        internal void OnEntered(CityBuilding building, bool hiding)
        {
            // 정착지 탐색 목표에 들어온 경우만 새 정착지로 확정
            stayElapsed = 0d;
            isHiding = hiding && building != Target && building != Settlement;

            if (!isHiding && Settlement == null && BuildingCatalog.IsResidential(building.Type) && building == Target)
            {
                SetSettlement(building);
                abandonedThreat = null;
            }

            if (building == Target)
            {
                Target = null;
            }

            // 정착지에 들어오면 비상분을 뺀 소지품 음식·음료를 재고로 저장
            if (building == Settlement)
            {
                foraging.Store(building);
            }
        }


        // 건물 내부 체류 판단과 섭취 후 퇴장 희망 여부 반환
        internal bool UpdateInside(CityBuilding building, double gameSeconds, int zombiesInView, Vector2 zombieCenter)
        {
            // 체류 시간에 비례해 건물 재고를 한 개씩 찾아내 파밍 체류가 끝날 때 수색 완료
            stayElapsed += gameSeconds;
            building.RevealSearch(stayElapsed / SearchSeconds);

            // 찾아낸 건물 재고와 비상 소지품 순으로 섭취하고 찾아낸 재고만 기억
            EatInBuilding(building);

            // 정착지는 좀비가 많으면 버리고 섭취로 해결하지 못하거나 비축이 부족하면 파밍 출발
            if (building == Settlement)
            {
                if (zombiesInView >= AbandonZombieCount)
                {
                    MarkDanger(building);
                    abandonedThreat = zombieCenter;
                    return true;
                }

                // 수색을 마치지 않은 정착지는 허기·갈증과 무관하게 모두 찾은 뒤 원하는 아이템이 없을 때만 출발
                if (!building.IsSearchComplete && stayElapsed < SearchSeconds)
                {
                    return false;
                }

                // 배고프지 않아도 정착지 재고와 소지품이 비축 목표보다 적으면 비축 파밍 시작
                if (!isForaging && !foraging.HasEnoughReserve(building))
                {
                    isForaging = true;
                }

                // 파밍 후보가 소진된 경우 체류 시간만큼 쉬고 다시 출발
                if (isForaging && isForageExhausted && stayElapsed < ForageStaySeconds)
                {
                    return false;
                }

                // 정착지 재고를 이미 확인한 파밍으로 기록해 출발 직후 정착지 재선택 방지
                if (isForaging)
                {
                    tripVisited.Add(building);
                }

                isForageExhausted = false;
                return isForaging;
            }

            // 숨은 건물은 일정 시간 머문 뒤 원래 목표 재개
            if (isHiding)
            {
                return stayElapsed >= HideStaySeconds;
            }

            // 파밍 건물은 체류 시간 후 섭취와 보충을 마치고 다음 목표로 이동
            if (stayElapsed < ForageStaySeconds)
            {
                return false;
            }

            foraging.Visit(building);
            tripVisited.Add(building);
            RefreshForaging();
            return true;
        }


        // 건물 재고와 비상 소지품 순으로 섭취 후 재고 기억과 파밍 상태 갱신
        private void EatInBuilding(CityBuilding building)
        {
            // 탐색 기준 이하에서 건물 재고 먼저 사용하고 비상 기준 이하일 때만 소지품 사용
            foraging.EatFromStockIfNeeded(building);
            foraging.EatFromInventory();
            foraging.Remember(building);
            RefreshForaging();
        }


        // 수치와 비축에 따른 파밍 시작과 종료 갱신
        private void RefreshForaging()
        {
            // 기준 이하이면 파밍 시작, 귀환 조건을 만족하면 종료
            if (foraging.NeedsSupplies)
            {
                isForaging = true;
            }
            else if (isForaging && IsTripComplete())
            {
                isForaging = false;
                isForageExhausted = false;
                tripVisited.Clear();
            }
        }


        // 정착 여부에 따른 파밍 귀환 조건 조회
        private bool IsTripComplete()
        {
            // 정착지가 있으면 두 수치 80 이상과 소지 칸 가득 또는 비축 목표 도달, 없으면 기존 종료 조건
            if (Settlement == null)
            {
                return foraging.IsSatisfied;
            }

            return foraging.IsFed && (foraging.IsInventoryFull || foraging.HasEnoughReserve(Settlement));
        }


        // 정착지 교체와 건물별 정착 인원 증감
        private void SetSettlement(CityBuilding building)
        {
            // 같은 정착지 재지정 시 인원 중복 증가 방지
            if (Settlement == building)
            {
                return;
            }

            if (Settlement != null)
            {
                Settlement.RemoveSettler();
            }

            Settlement = building;

            if (building != null)
            {
                building.AddSettler();
            }
        }


        // 탐색 기준 이하에서 이번 파밍 중 확인하지 않은 정착지 재고로 먼저 귀환할지 조회
        private bool ShouldEatAtSettlement()
        {
            // 비축 파밍은 정착지를 채우는 목적이므로 수치가 기준 이하일 때만 정착지 재고 사용
            return Settlement != null && foraging.NeedsSupplies && !tripVisited.Contains(Settlement) &&
                foraging.HasUsefulSupply(Settlement);
        }


        // 정착지 또는 현재 위치 기준 반경을 넓혀 가며 가까운 파밍 건물 선택
        private CityBuilding FindForageTarget(Vector2 position)
        {
            // 기억상 재고가 있거나 미방문인 위험하지 않은 건물만 후보로 사용하고 비축 대상인 자기 정착지는 제외
            Vector2 anchor = Settlement != null ? (Vector2)Settlement.Door : position;
            float radius = ForageSearchRadius;

            for (int step = 0; step <= ForageRadiusSteps; step++)
            {
                CityBuilding best = null;
                float bestDistance = float.PositiveInfinity;
                float radiusSquared = step == ForageRadiusSteps ? float.PositiveInfinity : radius * radius;

                foreach (var building in map.Buildings)
                {
                    Vector2 door = building.Door;

                    if (building == Settlement || (door - anchor).sqrMagnitude > radiusSquared || IsDangerous(building) ||
                        tripVisited.Contains(building) || !foraging.HasUsefulSupply(building))
                    {
                        continue;
                    }

                    float distance = (door - position).sqrMagnitude;

                    if (distance < bestDistance)
                    {
                        best = building;
                        bestDistance = distance;
                    }
                }

                if (best != null)
                {
                    return best;
                }

                radius *= 2f;
            }

            return null;
        }


        // 위험하지 않은 가장 가까운 주거 시설 선택과 이주 시 좀비 방향 회피
        private CityBuilding FindShelter(Vector2 position)
        {
            // 이주 직후에는 좀비 반대 방향 후보를 먼저 찾고 없으면 전체 후보 사용
            CityBuilding shelter = FindShelter(position, abandonedThreat);
            return shelter != null || !abandonedThreat.HasValue ? shelter : FindShelter(position, null);
        }


        // 방향 조건을 적용한 가장 가까운 주거 시설 조회
        private CityBuilding FindShelter(Vector2 position, Vector2? threat)
        {
            // 위협 방향과 같은 쪽의 후보는 제외
            CityBuilding best = null;
            float bestDistance = float.PositiveInfinity;
            Vector2 threatDirection = threat.HasValue ? threat.Value - position : Vector2.zero;

            foreach (var building in map.Buildings)
            {
                Vector2 offset = (Vector2)building.Door - position;

                if (!BuildingCatalog.IsResidential(building.Type) || IsDangerous(building) ||
                    (threat.HasValue && Vector2.Dot(offset, threatDirection) > 0f))
                {
                    continue;
                }

                float distance = offset.sqrMagnitude;

                if (distance < bestDistance)
                {
                    best = building;
                    bestDistance = distance;
                }
            }

            return best;
        }


        // 위험 표시 유지 여부 조회
        private bool IsDangerous(CityBuilding building)
        {
            // 기억 시간이 지난 표시는 위험하지 않은 것으로 판단
            return dangerUntil.TryGetValue(building, out float until) && Time.time < until;
        }
    }
}
