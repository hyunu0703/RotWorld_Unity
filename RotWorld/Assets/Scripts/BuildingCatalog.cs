using System;

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
    /// 구역별 건물 종류 배정과 한국어 표시 이름 제공
    /// </summary>
    public static class BuildingCatalog
    {
        // 다른 생성 난수 흐름을 소비하지 않는 지역별 시작점 해시
        private const uint SeedHashMultiplier = 73856093u;
        private const uint RegionHashMultiplier = 19349663u;

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


        // 같은 시드에서 구역별 종류를 순환 배정
        internal static BuildingType Select(CityRegion region, int seed, int ordinal)
        {
            // 지역별 시작 종류를 분리하고 후보 수 이상 배치 시 모든 종류 포함
            int regionIndex = (int)region;

            if (regionIndex < 0 || regionIndex >= RegionTypes.Length || ordinal < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(region));
            }

            var candidates = RegionTypes[regionIndex];
            uint offset = unchecked((uint)seed * SeedHashMultiplier + (uint)regionIndex * RegionHashMultiplier);
            return candidates[(int)((offset % (uint)candidates.Length + (uint)ordinal) % (uint)candidates.Length)];
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
    }
}
