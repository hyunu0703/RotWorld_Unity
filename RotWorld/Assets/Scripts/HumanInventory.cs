using System;
using System.Collections.Generic;

namespace RotWorld
{
    /// <summary>
    /// 인간별 소지품과 즉시 사용 및 가방 착용 관리
    /// </summary>
    public sealed class HumanInventory
    {
        // 한 칸당 한 개를 보관하는 기본 및 확장 용량
        public const int BaseCapacity = 5;
        public const int MaximumCapacity = 15;
        private readonly ItemDefinition[] slots = new ItemDefinition[MaximumCapacity];
        public IReadOnlyList<ItemDefinition> Slots { get; }
        public int Count { get; private set; }
        public int Version { get; private set; }

        // 소유 인간의 생존 수치와 별도 착용 가방
        private readonly HumanNeeds needs;
        public ItemDefinition EquippedBag { get; private set; }
        public int Capacity => BaseCapacity + (EquippedBag == null ? 0 : EquippedBag.AdditionalSlots);

        // 소유 인간과 읽기 전용 슬롯 목록 연결
        public HumanInventory(HumanNeeds needs)
        {
            // 필수 생존 수치 검증 후 외부 수정 방지 목록 제공
            this.needs = needs ?? throw new ArgumentNullException(nameof(needs));
            Slots = Array.AsReadOnly(slots);
        }


        // 빈 소지 칸에 아이템 한 개 추가
        public bool TryAdd(ItemDefinition item)
        {
            // 아이템 누락과 소지 한도 초과 차단
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            for (int i = 0; i < Capacity; i++)
            {
                if (slots[i] != null)
                {
                    continue;
                }

                // 첫 빈 칸에 한 개 보관 후 표시 변경 기록
                slots[i] = item;
                Count++;
                Version++;
                return true;
            }

            return false;
        }


        // 소지 칸을 거치지 않고 주운 가방을 바로 착용
        public bool TryEquip(ItemDefinition bag)
        {
            // 가방이 아닌 아이템과 중복 착용 차단
            if (bag == null)
            {
                throw new ArgumentNullException(nameof(bag));
            }

            if (bag != ItemCatalog.Bag || EquippedBag != null)
            {
                return false;
            }

            // 별도 장비 칸에 보관 후 표시 변경 기록
            EquippedBag = bag;
            Version++;
            return true;
        }


        // 소지 슬롯의 음식 소비 또는 가방 착용
        public bool TryUse(int slotIndex)
        {
            // 잠긴 칸과 빈 칸의 사용 차단
            if (slotIndex < 0 || slotIndex >= Capacity || slots[slotIndex] == null)
            {
                return false;
            }

            var item = slots[slotIndex];

            if (item == ItemCatalog.Bag)
            {
                // 가방 중복 착용 차단 및 별도 장비 칸으로 이동
                if (EquippedBag != null)
                {
                    return false;
                }

                EquippedBag = item;
            }
            else if (!needs.UseItem(item))
            {
                // 회복 불가 시 소지 수량 유지
                return false;
            }

            // 사용 성공 시 선택 칸의 한 개만 제거
            slots[slotIndex] = null;
            Count--;
            Version++;
            return true;
        }


        // 정착지 저장 등으로 넘긴 소지 칸의 아이템 한 개 제거
        public bool TryRemove(int slotIndex)
        {
            // 잠긴 칸과 빈 칸의 제거 차단
            if (slotIndex < 0 || slotIndex >= Capacity || slots[slotIndex] == null)
            {
                return false;
            }

            // 선택 칸을 비운 뒤 표시 변경 기록
            slots[slotIndex] = null;
            Count--;
            Version++;
            return true;
        }


        // 소지품을 보존할 수 있을 때 가방 해제
        public bool TryUnequipBag()
        {
            // 가방 반환 한 칸까지 기본 용량에 들어갈 때만 해제 허용
            if (EquippedBag == null || Count >= BaseCapacity)
            {
                return false;
            }

            // 확장 칸의 아이템을 기본 칸으로 압축한 뒤 가방 반환
            int destination = 0;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                {
                    continue;
                }

                var item = slots[i];
                slots[i] = null;
                slots[destination++] = item;
            }

            slots[destination] = EquippedBag;
            EquippedBag = null;
            Count++;
            Version++;
            return true;
        }
    }
}
