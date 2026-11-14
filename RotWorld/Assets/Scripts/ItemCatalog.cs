using System;
using System.Collections.Generic;

namespace RotWorld
{
    /// <summary>
    /// 음식 및 음료 6종의 고정 정보 제공
    /// </summary>
    public static class ItemCatalog
    {
        // 합의된 아이템별 이름과 분류 및 회복량
        public static IReadOnlyList<ItemDefinition> All { get; } = Array.AsReadOnly(new[]
        {
            new ItemDefinition("biscuit", "비스킷", ItemCategory.Food, 15, 0),
            new ItemDefinition("bread", "빵", ItemCategory.Food, 25, 0),
            new ItemDefinition("canned_food", "식품 통조림", ItemCategory.Food, 35, 0),
            new ItemDefinition("combat_ration", "전투식량", ItemCategory.Food, 50, 0),
            new ItemDefinition("water", "생수", ItemCategory.Drink, 0, 40),
            new ItemDefinition("fruit_juice", "과일 주스", ItemCategory.Drink, 10, 25)
        });
    }
}
