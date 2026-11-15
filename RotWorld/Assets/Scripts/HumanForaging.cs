using System;
using System.Collections.Generic;

namespace RotWorld
{
    /// <summary>
    /// 인간별 방문 건물 재고 기억과 음식·음료 섭취 및 파밍 규칙
    /// </summary>
    internal sealed class HumanForaging
    {
        // 섭취와 파밍 판단 기준 수치
        internal const double NeedThreshold = 40d;
        internal const double SatisfiedThreshold = 80d;

        // 소유 인간의 생존 수치와 소지품
        private readonly HumanNeeds needs;
        private readonly HumanInventory inventory;

        // 들어가 본 건물의 방문 시점 재고 스냅샷
        private readonly Dictionary<CityBuilding, int[]> memory = new Dictionary<CityBuilding, int[]>();

        // 소유 인간의 수치와 소지품 연결
        internal HumanForaging(HumanNeeds needs, HumanInventory inventory)
        {
            // 판단에 필요한 개인 상태 누락 차단
            this.needs = needs ?? throw new ArgumentNullException(nameof(needs));
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }


        // 허기 또는 갈증이 섭취 기준 이하인지 조회
        internal bool NeedsSupplies => needs.Hunger <= NeedThreshold || needs.Thirst <= NeedThreshold;

        // 두 수치와 소지품 비축이 파밍 종료 기준을 만족하는지 조회
        internal bool IsSatisfied => needs.Hunger >= SatisfiedThreshold && needs.Thirst >= SatisfiedThreshold &&
            HasCarriedItem(true) && HasCarriedItem(false);


        // 기억상 부족한 수치를 채울 아이템이 남아 있거나 아직 들어가 보지 않은 건물인지 조회
        internal bool HasUsefulSupply(CityBuilding building)
        {
            // 미방문 건물은 재고를 모르므로 탐색 후보로 유지
            if (!memory.TryGetValue(building, out int[] stock))
            {
                return true;
            }

            // 수치나 비축이 부족한 쪽을 채우는 아이템만 유효하고 둘 다 충분하면 모든 음식·음료 허용
            bool lacksHunger = needs.Hunger < SatisfiedThreshold || !HasCarriedItem(true);
            bool lacksThirst = needs.Thirst < SatisfiedThreshold || !HasCarriedItem(false);

            for (int i = 0; i < stock.Length && i < ItemCatalog.All.Count; i++)
            {
                var item = ItemCatalog.All[i];

                if (stock[i] > 0 && IsSupply(item) && ((!lacksHunger && !lacksThirst) ||
                    (lacksHunger && item.HungerRecovery > 0) || (lacksThirst && item.ThirstRecovery > 0)))
                {
                    return true;
                }
            }

            return false;
        }


        // 섭취 기준 이하인 수치만 더 낮은 쪽부터 소지품으로 채움
        internal void EatFromInventory()
        {
            // 기준을 넘거나 쓸 수 있는 소지품이 없을 때까지 한 개씩 사용
            while (NeedsSupplies)
            {
                bool isHungerLower = IsHungerLower();
                int slot = FindInventorySlot(isHungerLower);

                // 낮은 쪽을 채울 아이템이 없으면 다른 쪽도 기준 이하일 때만 사용
                if (slot < 0 && (isHungerLower ? needs.Thirst : needs.Hunger) <= NeedThreshold)
                {
                    slot = FindInventorySlot(!isHungerLower);
                }

                if (slot < 0 || !inventory.TryUse(slot))
                {
                    return;
                }
            }
        }


        // 섭취 기준 이하일 때 현재 건물 재고에서 섭취
        internal void EatFromStockIfNeeded(CityBuilding building)
        {
            // 기준 이하일 때만 상한을 넘기지 않는 범위에서 재고 사용
            if (building != null && NeedsSupplies)
            {
                EatFromStock(building);
            }
        }


        // 파밍 체류 종료 시 섭취와 소지품 보충 후 재고 기억 갱신
        internal void Visit(CityBuilding building)
        {
            // 바로 먹은 뒤 남은 재고에서 빈 칸만큼 가져가고 방문 시점 재고 기록
            if (building == null)
            {
                throw new ArgumentNullException(nameof(building));
            }

            EatFromStock(building);
            Loot(building);
            Remember(building);
        }


        // 현재 재고를 기억 스냅샷으로 저장
        internal void Remember(CityBuilding building)
        {
            // 기존 배열을 재사용해 반복 방문 시 할당 감소
            if (!memory.TryGetValue(building, out int[] stock))
            {
                stock = new int[building.Stock.Length];
                memory.Add(building, stock);
            }

            Array.Copy(building.Stock, stock, stock.Length);
        }


        // 낮은 수치부터 최대치를 넘겨 낭비하지 않는 재고 아이템 섭취
        private void EatFromStock(CityBuilding building)
        {
            // 두 수치 모두 넣을 아이템이 없을 때까지 한 개씩 사용
            while (true)
            {
                bool isHungerLower = IsHungerLower();
                int index = FindStockItem(building, isHungerLower, true);

                if (index < 0)
                {
                    index = FindStockItem(building, !isHungerLower, true);
                }

                if (index < 0 || !needs.UseItem(ItemCatalog.All[index]))
                {
                    return;
                }

                building.Stock[index]--;
            }
        }


        // 가방 착용 후 비축이 적은 수치를 채우는 아이템부터 빈 칸 보충
        private void Loot(CityBuilding building)
        {
            // 가방은 칸을 차지하지 않고 용량을 늘리므로 다른 아이템보다 먼저 착용
            int bagIndex = FindItemIndex(ItemCatalog.Bag);

            if (inventory.EquippedBag == null && bagIndex >= 0 && building.Stock[bagIndex] > 0 && inventory.TryEquip(ItemCatalog.Bag))
            {
                building.Stock[bagIndex]--;
            }

            // 현재 수치와 소지 회복량의 합이 낮은 쪽 아이템부터 한 칸씩 보충
            while (inventory.Count < inventory.Capacity)
            {
                bool isHungerLower = needs.Hunger + GetCarriedRecovery(true) <= needs.Thirst + GetCarriedRecovery(false);
                int index = FindStockItem(building, isHungerLower, false);

                if (index < 0)
                {
                    index = FindStockItem(building, !isHungerLower, false);
                }

                if (index < 0 || !inventory.TryAdd(ItemCatalog.All[index]))
                {
                    return;
                }

                building.Stock[index]--;
            }
        }


        // 지정 수치를 채우는 재고 아이템 중 회복량이 가장 큰 항목 조회
        private int FindStockItem(CityBuilding building, bool hunger, bool mustFit)
        {
            // 섭취 시에는 최대치 초과 아이템을 제외하고 파밍 시에는 모든 대상 아이템 허용
            double room = MaximumRoom(hunger);
            int best = -1;
            int bestRecovery = 0;

            for (int i = 0; i < building.Stock.Length && i < ItemCatalog.All.Count; i++)
            {
                var item = ItemCatalog.All[i];
                int recovery = GetRecovery(item, hunger);

                if (building.Stock[i] <= 0 || !IsSupply(item) || recovery <= 0 || (mustFit && recovery > room) || recovery <= bestRecovery)
                {
                    continue;
                }

                best = i;
                bestRecovery = recovery;
            }

            return best;
        }


        // 지정 수치를 채우는 소지품 중 최대치를 넘기지 않는 회복량이 가장 큰 칸 조회
        private int FindInventorySlot(bool hunger)
        {
            // 상한을 넘는 아이템만 남았으면 가장 작은 회복량 아이템으로 대체
            double room = MaximumRoom(hunger);
            int best = -1;
            int bestRecovery = 0;
            int fallback = -1;
            int fallbackRecovery = int.MaxValue;

            for (int i = 0; i < inventory.Capacity; i++)
            {
                var item = inventory.Slots[i];
                int recovery = item == null ? 0 : GetRecovery(item, hunger);

                if (recovery <= 0 || !IsSupply(item))
                {
                    continue;
                }

                if (recovery <= room && recovery > bestRecovery)
                {
                    best = i;
                    bestRecovery = recovery;
                }

                if (recovery < fallbackRecovery)
                {
                    fallback = i;
                    fallbackRecovery = recovery;
                }
            }

            return best >= 0 ? best : fallback;
        }


        // 지정 수치를 채우는 소지품 보유 여부 조회
        private bool HasCarriedItem(bool hunger)
        {
            // 가방 등 회복량이 없는 장비 제외
            for (int i = 0; i < inventory.Capacity; i++)
            {
                var item = inventory.Slots[i];

                if (item != null && IsSupply(item) && GetRecovery(item, hunger) > 0)
                {
                    return true;
                }
            }

            return false;
        }


        // 소지품의 지정 수치 회복량 합계 조회
        private int GetCarriedRecovery(bool hunger)
        {
            // 파밍 우선순위 비교에 사용할 현재 비축량 계산
            int total = 0;

            for (int i = 0; i < inventory.Capacity; i++)
            {
                var item = inventory.Slots[i];

                if (item != null && IsSupply(item))
                {
                    total += GetRecovery(item, hunger);
                }
            }

            return total;
        }


        // 허기가 갈증 이하인지 조회
        private bool IsHungerLower()
        {
            // 동률이면 허기 우선
            return needs.Hunger <= needs.Thirst;
        }


        // 지정 수치의 최대치까지 남은 회복 여유 조회
        private double MaximumRoom(bool hunger)
        {
            // 아이템 회복 상한 기준 남은 양 계산
            return HumanNeeds.MaximumValue - (hunger ? needs.Hunger : needs.Thirst);
        }


        // 아이템의 지정 수치 회복량 조회
        private static int GetRecovery(ItemDefinition item, bool hunger)
        {
            // 허기와 갈증 회복량 중 요청 항목 반환
            return hunger ? item.HungerRecovery : item.ThirstRecovery;
        }


        // 허기·갈증을 채우는 음식과 음료 여부 조회
        private static bool IsSupply(ItemDefinition item)
        {
            // 가방 등 장비는 섭취와 비축 대상에서 제외
            return item.Category == ItemCategory.Food || item.Category == ItemCategory.Drink;
        }


        // 카탈로그 재고 순서의 아이템 위치 조회
        private static int FindItemIndex(ItemDefinition item)
        {
            // 재고 배열은 ItemCatalog.All 순서를 따름
            for (int i = 0; i < ItemCatalog.All.Count; i++)
            {
                if (ItemCatalog.All[i] == item)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
