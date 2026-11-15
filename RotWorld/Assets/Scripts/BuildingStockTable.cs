using System;
using System.Collections.Generic;

namespace RotWorld
{
    /// <summary>
    /// 건물 종류별 아이템 수량 범위와 가방 출현 확률로 초기 재고 생성
    /// </summary>
    internal static class BuildingStockTable
    {
        // ItemCatalog.All 순서와 일치시킨 재고 배열 인덱스
        private const int BiscuitIndex = 0;
        private const int BreadIndex = 1;
        private const int CannedFoodIndex = 2;
        private const int CombatRationIndex = 3;
        private const int WaterIndex = 4;
        private const int FruitJuiceIndex = 5;
        private const int BagIndex = 6;
        private static readonly string[] ExpectedCodes =
        {
            "biscuit", "bread", "canned_food", "combat_ration", "water", "fruit_juice", "bag"
        };

        // 물자량 설정의 백분율 기준과 가방 확률 기준
        private const double SupplyPercentageBase = 100d;
        private const int PercentRange = 100;

        // 레벨디자인 v1 기준 건물 종류별 재고 규칙
        private static readonly Dictionary<BuildingType, StockRule> Rules = new Dictionary<BuildingType, StockRule>
        {
            // 복합 시가지
            { BuildingType.DetachedHouse, new StockRule(biscuit: Range(0, 2), bread: Range(0, 2), cannedFood: Range(1, 3), water: Range(1, 3), fruitJuice: Range(0, 2), bagPercent: 10) },
            { BuildingType.Villa, new StockRule(biscuit: Range(0, 2), bread: Range(0, 1), cannedFood: Range(0, 2), water: Range(1, 2), fruitJuice: Range(0, 1), bagPercent: 5) },
            { BuildingType.ConvenienceStore, new StockRule(biscuit: Range(3, 6), bread: Range(2, 4), cannedFood: Range(1, 3), water: Range(4, 8), fruitJuice: Range(3, 6)) },
            { BuildingType.NeighborhoodMarket, new StockRule(biscuit: Range(2, 4), bread: Range(1, 3), cannedFood: Range(4, 8), water: Range(4, 7), fruitJuice: Range(2, 4)) },
            { BuildingType.Restaurant, new StockRule(bread: Range(1, 3), cannedFood: Range(2, 4), water: Range(2, 4), fruitJuice: Range(0, 2)) },
            { BuildingType.Pharmacy, new StockRule(biscuit: Range(0, 2), water: Range(1, 3), fruitJuice: Range(1, 3)) },
            { BuildingType.HardwareStore, new StockRule(biscuit: Range(0, 1), water: Range(0, 2), bagPercent: 15) },

            // 분산된 마을
            { BuildingType.CountryHouse, new StockRule(biscuit: Range(0, 1), bread: Range(0, 1), cannedFood: Range(2, 4), water: Range(1, 3), fruitJuice: Range(0, 1), bagPercent: 10) },
            { BuildingType.SmallSupermarket, new StockRule(biscuit: Range(2, 4), bread: Range(1, 3), cannedFood: Range(3, 6), water: Range(3, 6), fruitJuice: Range(1, 3)) },
            { BuildingType.VillageHall, new StockRule(biscuit: Range(1, 3), cannedFood: Range(0, 2), water: Range(2, 4), fruitJuice: Range(1, 3), bagPercent: 5) },
            { BuildingType.FarmToolStorage, new StockRule(cannedFood: Range(0, 1), water: Range(0, 1), bagPercent: 10) },
            { BuildingType.HealthCenter, new StockRule(biscuit: Range(0, 1), water: Range(2, 4), fruitJuice: Range(0, 2)) },

            // 고밀도 도심
            { BuildingType.Apartment, new StockRule(biscuit: Range(1, 4), bread: Range(1, 3), cannedFood: Range(2, 5), water: Range(2, 5), fruitJuice: Range(1, 3), bagPercent: 20) },
            { BuildingType.OfficeBuilding, new StockRule(biscuit: Range(2, 4), bread: Range(0, 1), water: Range(3, 6), fruitJuice: Range(0, 2), bagPercent: 15) },
            { BuildingType.Hypermarket, new StockRule(biscuit: Range(6, 10), bread: Range(4, 8), cannedFood: Range(8, 14), combatRation: Range(0, 1), water: Range(10, 16), fruitJuice: Range(5, 9), bagPercent: 30) },
            { BuildingType.Hotel, new StockRule(biscuit: Range(1, 3), bread: Range(1, 3), water: Range(3, 6), fruitJuice: Range(2, 4), bagPercent: 40) },
            { BuildingType.Hospital, new StockRule(biscuit: Range(1, 3), bread: Range(0, 2), water: Range(3, 6), fruitJuice: Range(2, 4)) },
            { BuildingType.DepartmentStore, new StockRule(biscuit: Range(2, 4), bread: Range(2, 4), cannedFood: Range(1, 3), water: Range(2, 4), fruitJuice: Range(2, 4), bagPercent: 100) },
            { BuildingType.PoliceStation, new StockRule(biscuit: Range(0, 2), combatRation: Range(2, 4), water: Range(3, 5), bagPercent: 30) },

            // 강변 도시
            { BuildingType.RiversideHouse, new StockRule(biscuit: Range(0, 2), bread: Range(0, 2), cannedFood: Range(1, 3), water: Range(1, 3), fruitJuice: Range(0, 2), bagPercent: 10) },
            { BuildingType.Cafe, new StockRule(biscuit: Range(1, 3), bread: Range(2, 4), water: Range(2, 4), fruitJuice: Range(3, 6)) },
            { BuildingType.RiversideRestaurant, new StockRule(bread: Range(1, 2), cannedFood: Range(2, 5), water: Range(2, 4), fruitJuice: Range(1, 2)) },
            { BuildingType.SmallWarehouse, new StockRule(cannedFood: Range(0, 4), water: Range(0, 4), bagPercent: 10) },
            { BuildingType.FishingSupplyStore, new StockRule(biscuit: Range(1, 2), cannedFood: Range(1, 3), combatRation: Range(0, 2), water: Range(2, 4), bagPercent: 60) },
            { BuildingType.WaterfrontOffice, new StockRule(biscuit: Range(0, 2), cannedFood: Range(0, 1), combatRation: Range(0, 1), water: Range(2, 3), bagPercent: 5) },

            // 산업 외곽
            { BuildingType.Factory, new StockRule(biscuit: Range(0, 2), cannedFood: Range(0, 1), water: Range(1, 3), bagPercent: 5) },
            { BuildingType.LogisticsWarehouse, new StockRule(biscuit: Range(0, 6), cannedFood: Range(0, 8), water: Range(0, 8), fruitJuice: Range(0, 4), bagPercent: 20) },
            { BuildingType.RepairShop, new StockRule(biscuit: Range(0, 1), water: Range(1, 2), fruitJuice: Range(0, 1), bagPercent: 10) },
            { BuildingType.StaffDormitory, new StockRule(biscuit: Range(1, 3), bread: Range(0, 2), cannedFood: Range(2, 4), water: Range(2, 4), fruitJuice: Range(0, 2), bagPercent: 15) },
            { BuildingType.ToolStore, new StockRule(water: Range(0, 1), bagPercent: 25) },
            { BuildingType.Guardhouse, new StockRule(biscuit: Range(0, 2), combatRation: Range(0, 1), water: Range(1, 2)) },

            // 자연·미개발 공간
            { BuildingType.Cabin, new StockRule(biscuit: Range(0, 2), cannedFood: Range(1, 3), combatRation: Range(0, 2), water: Range(2, 4), bagPercent: 20) },
            { BuildingType.CampsiteOffice, new StockRule(biscuit: Range(1, 3), cannedFood: Range(1, 3), combatRation: Range(0, 1), water: Range(3, 5), fruitJuice: Range(1, 2), bagPercent: 25) },
            { BuildingType.PublicRestroom, new StockRule(water: Range(0, 1)) },
            { BuildingType.CoastalOutpost, new StockRule(combatRation: Range(1, 3), water: Range(2, 3), bagPercent: 15) },
        };

        // 재고 배열의 아이템 수
        internal static int SlotCount => ExpectedCodes.Length;


        // 카탈로그 순서 변경 시 재고 인덱스 불일치 차단
        static BuildingStockTable()
        {
            // ItemCatalog.All과 재고 인덱스의 코드 대응 검증
            if (ItemCatalog.All.Count != ExpectedCodes.Length)
            {
                throw new InvalidOperationException("건물 재고 인덱스와 아이템 카탈로그 수량 불일치");
            }

            for (int i = 0; i < ExpectedCodes.Length; i++)
            {
                if (ItemCatalog.All[i].Code != ExpectedCodes[i])
                {
                    throw new InvalidOperationException($"건물 재고 인덱스 {i}와 아이템 코드 {ItemCatalog.All[i].Code} 불일치");
                }
            }
        }


        // 건물 종류와 물자량 설정으로 ItemCatalog.All 순서의 재고 생성
        internal static int[] Create(BuildingType type, System.Random random, int supplies)
        {
            // 난수 누락과 종류 미배정 및 규칙 누락 차단
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            if (!Rules.TryGetValue(type, out StockRule rule))
            {
                throw new InvalidOperationException($"재고 규칙이 없는 건물 종류 {type}");
            }

            // 소모품은 범위 난수에 물자량 배율을 곱한 뒤 확률 반올림
            var stock = new int[SlotCount];
            double scale = supplies / SupplyPercentageBase;
            stock[BiscuitIndex] = Roll(rule.Biscuit, scale, random);
            stock[BreadIndex] = Roll(rule.Bread, scale, random);
            stock[CannedFoodIndex] = Roll(rule.CannedFood, scale, random);
            stock[CombatRationIndex] = Roll(rule.CombatRation, scale, random);
            stock[WaterIndex] = Roll(rule.Water, scale, random);
            stock[FruitJuiceIndex] = Roll(rule.FruitJuice, scale, random);

            // 가방은 물자량 배율 없이 확률 판정하되 물자 없음 설정에서는 제외
            bool hasBag = random.Next(PercentRange) < rule.BagPercent;
            stock[BagIndex] = hasBag && supplies > 0 ? 1 : 0;
            return stock;
        }


        // 범위 난수와 배율에서 기대값을 보존하는 정수 수량 산출
        private static int Roll(ItemRange range, double scale, System.Random random)
        {
            // 범위 여부와 무관하게 같은 횟수의 난수를 소비해 재현성 유지
            int baseCount = random.Next(range.Min, range.Max + 1);
            double roundingRoll = random.NextDouble();

            // 소수 부분을 확률로 올려 낮은 물자량에서 소량 항목 소실 방지
            return (int)Math.Floor(baseCount * scale + roundingRoll);
        }


        // 최소와 최대 수량 범위 생성
        private static ItemRange Range(int min, int max)
        {
            // 잘못된 표 데이터의 재고 생성 차단
            if (min < 0 || max < min)
            {
                throw new ArgumentOutOfRangeException(nameof(max));
            }

            return new ItemRange(min, max);
        }


        /// <summary>
        /// 아이템 한 종의 최소 및 최대 배치 수량
        /// </summary>
        private readonly struct ItemRange
        {
            // 포함 범위의 양 끝 수량
            public int Min { get; }
            public int Max { get; }

            // 검증된 범위 보관
            public ItemRange(int min, int max)
            {
                // 표 생성 시 검증된 값 그대로 보관
                Min = min;
                Max = max;
            }
        }


        /// <summary>
        /// 건물 종류 하나의 아이템별 수량 범위와 가방 출현 확률
        /// </summary>
        private sealed class StockRule
        {
            // 음식과 음료별 수량 범위
            public ItemRange Biscuit { get; }
            public ItemRange Bread { get; }
            public ItemRange CannedFood { get; }
            public ItemRange CombatRation { get; }
            public ItemRange Water { get; }
            public ItemRange FruitJuice { get; }

            // 가방 한 개의 출현 확률 백분율
            public int BagPercent { get; }

            // 생략한 아이템은 0개 범위로 보관
            public StockRule(ItemRange biscuit = default, ItemRange bread = default, ItemRange cannedFood = default,
                ItemRange combatRation = default, ItemRange water = default, ItemRange fruitJuice = default,
                int bagPercent = 0)
            {
                // 확률 범위를 벗어난 표 데이터 차단
                if (bagPercent < 0 || bagPercent > PercentRange)
                {
                    throw new ArgumentOutOfRangeException(nameof(bagPercent));
                }

                Biscuit = biscuit;
                Bread = bread;
                CannedFood = cannedFood;
                CombatRation = combatRation;
                Water = water;
                FruitJuice = fruitJuice;
                BagPercent = bagPercent;
            }
        }
    }
}
