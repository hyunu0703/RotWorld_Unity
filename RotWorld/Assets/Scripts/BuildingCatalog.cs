using System;
using System.Collections.Generic;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 외형 분류와 별개로 구분하는 실제 건물 종류
    /// </summary>
    public enum BuildingType : byte
    {
        Unspecified,
        DetachedHouse,
        Villa,
        ConvenienceStore,
        NeighborhoodMarket,
        Pharmacy,
        Restaurant,
        HardwareStore,
        CountryHouse,
        VillageHall,
        SmallSupermarket,
        HealthCenter,
        FarmToolStorage,
        Apartment,
        OfficeBuilding,
        Hypermarket,
        Hospital,
        DepartmentStore,
        Hotel,
        PoliceStation,
        RiversideHouse,
        Cafe,
        RiversideRestaurant,
        FishingSupplyStore,
        WaterfrontOffice,
        SmallWarehouse,
        Factory,
        LogisticsWarehouse,
        RepairShop,
        ToolStore,
        StaffDormitory,
        Guardhouse,
        Cabin,
        CampsiteOffice,
        PublicRestroom,
        CoastalOutpost,
    }

    /// <summary>
    /// 구역별 건물 종류 배정과 한국어 표시 이름 및 주거 시설 저장 용량 제공
    /// </summary>
    public static class BuildingCatalog
    {
        // 다른 생성 난수 흐름을 소비하지 않는 지역별 시작점 해시
        private const uint SeedHashMultiplier = 73856093u;
        private const uint RegionHashMultiplier = 19349663u;

        // 정착지 저장 용량의 필지 크기 보정 범위
        private const float MinimumStorageScale = .75f;
        private const float StorageScaleRange = .5f;
        private const float NeutralSizeRatio = .5f;

        // 주거 시설 종류별 정착지 저장 기본 용량
        private const int CabinStorage = 15;
        private const int CountryHouseStorage = 20;
        private const int DetachedHouseStorage = 25;
        private const int RiversideHouseStorage = 25;
        private const int VillaStorage = 30;
        private const int StaffDormitoryStorage = 40;
        private const int ApartmentStorage = 50;

        // 소규모 생활권 구성 기준과 실제 바다 인접 판정 거리
        private const int SmallGroupLimit = 3;
        private const int MediumGroupLimit = 8;
        private const int CoastDistance = 12;
        private const int CoastalTypeIndex = 3;

        // CityRegion 순서에 맞춘 구역별 생성 후보
        private static readonly BuildingType[][] RegionTypes =
        {
            new[] { BuildingType.DetachedHouse, BuildingType.Villa, BuildingType.ConvenienceStore, BuildingType.NeighborhoodMarket, BuildingType.Pharmacy, BuildingType.Restaurant, BuildingType.HardwareStore },
            new[] { BuildingType.CountryHouse, BuildingType.VillageHall, BuildingType.SmallSupermarket, BuildingType.HealthCenter, BuildingType.FarmToolStorage },
            new[] { BuildingType.Apartment, BuildingType.OfficeBuilding, BuildingType.Hypermarket, BuildingType.Hospital, BuildingType.DepartmentStore, BuildingType.Hotel, BuildingType.PoliceStation },
            new[] { BuildingType.RiversideHouse, BuildingType.Cafe, BuildingType.RiversideRestaurant, BuildingType.FishingSupplyStore, BuildingType.WaterfrontOffice, BuildingType.SmallWarehouse },
            new[] { BuildingType.Factory, BuildingType.LogisticsWarehouse, BuildingType.RepairShop, BuildingType.ToolStore, BuildingType.StaffDormitory, BuildingType.Guardhouse },
            new[] { BuildingType.Cabin, BuildingType.CampsiteOffice, BuildingType.PublicRestroom, BuildingType.CoastalOutpost },
        };

        // 구역별 목표 비율과 대표 및 생활 및 특색 시설의 배정 단계
        private static readonly int[][] RegionWeights =
        {
            new[] { 30, 15, 15, 12, 8, 12, 8 },
            new[] { 40, 15, 20, 10, 15 },
            new[] { 30, 20, 15, 10, 8, 12, 5 },
            new[] { 35, 15, 15, 10, 10, 15 },
            new[] { 30, 25, 15, 10, 15, 5 },
            new[] { 40, 25, 25, 10 }
        };
        private static readonly int[][] RegionTiers =
        {
            new[] { 0, 0, 1, 1, 2, 2, 2 },
            new[] { 0, 1, 1, 2, 2 },
            new[] { 0, 0, 1, 2, 2, 2, 2 },
            new[] { 0, 1, 1, 2, 2, 2 },
            new[] { 0, 0, 1, 2, 1, 2 },
            new[] { 0, 1, 2, 2 }
        };

        // BuildingType 순서에 맞춘 UI 표시 이름
        private static readonly string[] Names =
        {
            "건물",
            "단독주택",
            "빌라",
            "편의점",
            "동네 마트",
            "약국",
            "식당",
            "철물점",
            "시골집",
            "마을회관",
            "작은 슈퍼",
            "보건소",
            "농기구 창고",
            "아파트",
            "사무실 빌딩",
            "대형마트",
            "병원",
            "백화점",
            "호텔",
            "경찰서",
            "강변 주택",
            "카페",
            "음식점",
            "낚시용품점",
            "수변 관리사무소",
            "소형 창고",
            "공장",
            "물류창고",
            "정비소",
            "공구상",
            "직원 기숙사",
            "경비실",
            "산장",
            "캠핑장 관리동",
            "공중화장실",
            "해안 관리초소",
        };


        // 실제 건물 수와 구역 및 개별 마을을 기준으로 종류 배정
        internal static void AssignTypes(CityMap map)
        {
            // 필지 생성 결과를 지역별 그룹과 가장 가까운 마을 중심별 그룹으로 분리
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            int regionCount = RegionTypes.Length;
            var groups = new List<int>[regionCount + map.VillageCenters.Count];

            for (int i = 0; i < groups.Length; i++)
            {
                groups[i] = new List<int>();
            }

            for (int i = 0; i < map.Buildings.Count; i++)
            {
                Vector2 center = map.Buildings[i].Bounds.center;
                var region = map.Regions[(int)center.y * map.Size + (int)center.x];
                int group = (int)region;

                if (region == CityRegion.Villages)
                {
                    group = regionCount + FindVillage(map, center);
                }

                groups[group].Add(i);
            }

            // 마을별로 독립된 시드 흐름을 사용하고 기존 건물 목록 순서 보존
            for (int i = 0; i < groups.Length; i++)
            {
                var region = i < regionCount ? (CityRegion)i : CityRegion.Villages;
                int seed = unchecked((int)((uint)map.Seed * SeedHashMultiplier + (uint)i * RegionHashMultiplier));
                AssignGroup(map, groups[i], region, new System.Random(seed));
            }
        }


        // 건물 중심에서 가장 가까운 마을의 고정 인덱스 조회
        private static int FindVillage(CityMap map, Vector2 center)
        {
            // 마을 계획이 누락된 지도에서 임의의 전역 배정 방지
            if (map.VillageCenters.Count == 0)
            {
                throw new InvalidOperationException("마을 건물의 종류 배정을 위한 마을 중심 누락");
            }

            int closest = 0;
            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < map.VillageCenters.Count; i++)
            {
                float distance = ((Vector2)map.VillageCenters[i] - center).sqrMagnitude;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    closest = i;
                }
            }

            return closest;
        }


        // 그룹별 비율 수량을 확정하고 지형 조건에 맞는 건물에 연결
        private static void AssignGroup(CityMap map, List<int> buildings, CityRegion region, System.Random random)
        {
            // 실제 배치된 건물이 없는 그룹의 난수와 재배정 생략
            if (buildings.Count == 0)
            {
                return;
            }

            var candidates = RegionTypes[(int)region];
            var capacities = new int[candidates.Length];
            var coastal = new bool[buildings.Count];

            for (int i = 0; i < capacities.Length; i++)
            {
                capacities[i] = buildings.Count;
            }

            // 해안 초소 후보를 실제 바다와의 거리로 제한하고 강변 제외
            if (region == CityRegion.Natural)
            {
                capacities[CoastalTypeIndex] = 0;

                for (int i = 0; i < buildings.Count; i++)
                {
                    coastal[i] = IsNearCoast(map, map.Buildings[buildings[i]].Bounds);

                    if (coastal[i])
                    {
                        capacities[CoastalTypeIndex]++;
                    }
                }
            }

            int[] counts = AllocateCounts(region, buildings.Count, capacities, random);
            var assigned = new BuildingType[buildings.Count];
            int[] order = ShuffledIndices(buildings.Count, random);

            // 제한 후보가 있는 초소부터 배정해 일반 시설이 해안 부지를 선점하는 상황 방지
            if (region == CityRegion.Natural)
            {
                foreach (int index in order)
                {
                    if (coastal[index] && counts[CoastalTypeIndex] > 0)
                    {
                        assigned[index] = BuildingType.CoastalOutpost;
                        counts[CoastalTypeIndex]--;
                    }
                }
            }

            // 나머지 건물에는 확정 수량을 배정하고 외형과 출입구 및 재고 참조 유지
            int nextType = 0;

            foreach (int index in order)
            {
                if (assigned[index] == BuildingType.Unspecified)
                {
                    while (counts[nextType] == 0)
                    {
                        nextType++;
                    }

                    assigned[index] = candidates[nextType];
                    counts[nextType]--;
                }

                int buildingIndex = buildings[index];
                var building = map.Buildings[buildingIndex];
                map.Buildings[buildingIndex] = building.WithType(assigned[index]);
            }
        }


        // 건물 수별 최소 구성과 목표 비율의 잔여량으로 정수 수량 산출
        private static int[] AllocateCounts(CityRegion region, int total, int[] capacities, System.Random random)
        {
            // 소규모 그룹의 특색 시설을 제외하고 지형상 가능한 후보의 비율 재정규화
            var weights = RegionWeights[(int)region];
            var tiers = RegionTiers[(int)region];
            var counts = new int[weights.Length];
            int weightSum = 0;

            for (int i = 0; i < weights.Length; i++)
            {
                if (total <= SmallGroupLimit && tiers[i] == 2)
                {
                    capacities[i] = 0;
                }

                if (capacities[i] > 0)
                {
                    weightSum += weights[i];
                }
            }

            // 해안 후보 수가 목표보다 적으면 초과 몫을 나머지 종류의 비율로 재배분
            var targets = new double[weights.Length];
            var capped = new bool[weights.Length];
            int remaining = total;
            bool hasNewCap;

            do
            {
                hasNewCap = false;

                for (int i = 0; i < weights.Length; i++)
                {
                    if (capacities[i] > 0 && !capped[i] && remaining * (double)weights[i] / weightSum > capacities[i])
                    {
                        targets[i] = capacities[i];
                        remaining -= capacities[i];
                        weightSum -= weights[i];
                        capped[i] = true;
                        hasNewCap = true;
                        break;
                    }
                }
            } while (hasNewCap);

            for (int i = 0; i < weights.Length; i++)
            {
                if (capacities[i] > 0 && !capped[i])
                {
                    targets[i] = remaining * (double)weights[i] / weightSum;
                }
            }

            // 작은 생활권의 대표 및 생활 시설과 중간 생활권의 특색 시설 우선 확보
            int allocated = 0;

            if (total <= MediumGroupLimit)
            {
                allocated += ReserveTier(0, weights, tiers, capacities, counts, random);

                if (total >= 2)
                {
                    allocated += ReserveTier(1, weights, tiers, capacities, counts, random);
                }

                if (total > SmallGroupLimit)
                {
                    allocated += ReserveTier(2, weights, tiers, capacities, counts, random);
                }
            }

            // 목표 수량에서 현재 수량을 뺀 잔여량 순으로 배정하며 동률만 시드 순서 적용
            int[] order = ShuffledIndices(weights.Length, random);

            while (allocated < total)
            {
                int best = -1;
                double bestDeficit = double.NegativeInfinity;

                foreach (int i in order)
                {
                    if (counts[i] >= capacities[i])
                    {
                        continue;
                    }

                    double deficit = targets[i] - counts[i];

                    if (deficit > bestDeficit)
                    {
                        best = i;
                        bestDeficit = deficit;
                    }
                }

                if (best < 0)
                {
                    throw new InvalidOperationException("건물 종류 배정 후보 수량 부족");
                }

                counts[best]++;
                allocated++;
            }

            return counts;
        }


        // 같은 우선 단계의 시설 중 목표 비율에 따른 한 종류 확보
        private static int ReserveTier(int tier, int[] weights, int[] tiers, int[] capacities,
            int[] counts, System.Random random)
        {
            // 실제 배치 가능한 후보만 가중 추첨에 포함
            int sum = 0;

            for (int i = 0; i < weights.Length; i++)
            {
                if (tiers[i] == tier && counts[i] < capacities[i])
                {
                    sum += weights[i];
                }
            }

            if (sum == 0)
            {
                return 0;
            }

            int choice = random.Next(sum);

            for (int i = 0; i < weights.Length; i++)
            {
                if (tiers[i] != tier || counts[i] >= capacities[i])
                {
                    continue;
                }

                choice -= weights[i];

                if (choice < 0)
                {
                    counts[i]++;
                    return 1;
                }
            }

            throw new InvalidOperationException("건물 종류 가중 추첨 실패");
        }


        // 다른 생성 난수 흐름에 영향을 주지 않는 인덱스 순서 섞기
        private static int[] ShuffledIndices(int count, System.Random random)
        {
            // 원래 목록을 변경하지 않는 피셔 예이츠 순열 구성
            var order = new int[count];

            for (int i = 0; i < count; i++)
            {
                order[i] = i;
            }

            for (int i = count - 1; i > 0; i--)
            {
                int next = random.Next(i + 1);
                int previous = order[i];
                order[i] = order[next];
                order[next] = previous;
            }

            return order;
        }


        // 건물 부지에서 일정 거리 안의 실제 해안 존재 여부 확인
        private static bool IsNearCoast(CityMap map, RectInt bounds)
        {
            // 원형 거리 안의 바다와 얕은 바다만 인정하고 강과 지도 밖 영역 제외
            int minX = Math.Max(0, bounds.xMin - CoastDistance);
            int maxX = Math.Min(map.Size - 1, bounds.xMax - 1 + CoastDistance);
            int minY = Math.Max(0, bounds.yMin - CoastDistance);
            int maxY = Math.Min(map.Size - 1, bounds.yMax - 1 + CoastDistance);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    byte terrain = map.Terrain[y * map.Size + x];

                    if (terrain != CityGenerator.Ocean && terrain != CityGenerator.Shallows)
                    {
                        continue;
                    }

                    int dx = Math.Max(Math.Max(bounds.xMin - x, x - (bounds.xMax - 1)), 0);
                    int dy = Math.Max(Math.Max(bounds.yMin - y, y - (bounds.yMax - 1)), 0);

                    if (dx * dx + dy * dy <= CoastDistance * CoastDistance)
                    {
                        return true;
                    }
                }
            }

            return false;
        }


        // 건물 종류의 한국어 이름 조회
        public static string GetName(BuildingType type)
        {
            // 유효하지 않은 외부 입력의 표시 요청 차단
            int index = (int)type;

            if (index < 0 || index >= Names.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(type));
            }

            return Names[index];
        }


        // 인간이 정착지로 사용할 수 있는 주거 시설 종류 조회
        public static bool IsResidential(BuildingType type)
        {
            // 기획에서 정한 주거 시설 일곱 종류만 정착 후보로 허용
            switch (type)
            {
                case BuildingType.DetachedHouse:
                case BuildingType.Villa:
                case BuildingType.CountryHouse:
                case BuildingType.Apartment:
                case BuildingType.RiversideHouse:
                case BuildingType.StaffDormitory:
                case BuildingType.Cabin:
                    return true;
                default:
                    return false;
            }
        }


        // 주거 시설 기본값과 필지 크기 보정으로 정착지 저장 용량 계산
        public static int GetStorageCapacity(BuildingType type, int area, int minimumArea, int maximumArea)
        {
            // 주거 시설이 아니면 저장 용량 없음
            int baseCapacity = GetStorageBase(type);

            if (baseCapacity == 0)
            {
                return 0;
            }

            // 지역 최소·최대 면적 사이 위치를 0~1로 제한하고 범위가 없는 고정 규격은 중간값 사용
            float ratio = maximumArea > minimumArea
                ? Mathf.Clamp01((area - minimumArea) / (float)(maximumArea - minimumArea)) : NeutralSizeRatio;
            float scale = MinimumStorageScale + StorageScaleRange * ratio;
            return Math.Max(1, Mathf.RoundToInt(baseCapacity * scale));
        }


        // 주거 시설 종류별 정착지 저장 기본 용량 조회
        private static int GetStorageBase(BuildingType type)
        {
            // 주거 시설 일곱 종류 외에는 0
            switch (type)
            {
                case BuildingType.Cabin:
                    return CabinStorage;
                case BuildingType.CountryHouse:
                    return CountryHouseStorage;
                case BuildingType.DetachedHouse:
                    return DetachedHouseStorage;
                case BuildingType.RiversideHouse:
                    return RiversideHouseStorage;
                case BuildingType.Villa:
                    return VillaStorage;
                case BuildingType.StaffDormitory:
                    return StaffDormitoryStorage;
                case BuildingType.Apartment:
                    return ApartmentStorage;
                default:
                    return 0;
            }
        }
    }
}
