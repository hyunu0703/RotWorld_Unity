namespace RotWorld
{
    /// <summary>
    /// 아이템의 음식 및 음료와 장비 분류
    /// </summary>
    public enum ItemCategory
    {
        Food,
        Drink,
        Equipment
    }

    /// <summary>
    /// 아이템 식별 정보와 허기 및 갈증 회복량 보관
    /// </summary>
    public sealed class ItemDefinition
    {
        // 아이템 식별과 표시 정보
        public string Code { get; }
        public string Name { get; }
        public ItemCategory Category { get; }
        public bool IsSupply => Category == ItemCategory.Food || Category == ItemCategory.Drink;

        // 사용 시 적용할 생존 수치 회복량
        public int HungerRecovery { get; }
        public int ThirstRecovery { get; }

        // 착용 시 추가할 소지 칸 수
        public int AdditionalSlots { get; }

        // 카탈로그의 고정 아이템 정보 보관
        internal ItemDefinition(string code, string name, ItemCategory category, int hungerRecovery, int thirstRecovery,
            int additionalSlots = 0)
        {
            // 카탈로그 정의값을 외부에서 변경할 수 없는 정보로 전달
            Code = code;
            Name = name;
            Category = category;
            HungerRecovery = hungerRecovery;
            ThirstRecovery = thirstRecovery;
            AdditionalSlots = additionalSlots;
        }
    }
}
