using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ForgottenQuests;

/// <summary>
/// Безопасное JSON-представление предмета из слота квеста.
/// Нельзя сохранять ItemStack напрямую: внутри есть Collectible, Textures и baked-данные,
/// что вызывает self-referencing loop при JsonUtil.ToString().
/// </summary>
public class QuestItemStackData
{
    public string Code { get; set; } = "";
    public string Class { get; set; } = "Item";
    public int StackSize { get; set; } = 1;

    // Для слотов существ: настоящий ID существа может храниться не в Collectible.Code,
    // а в атрибутах предмета из креативного инвентаря.
    public string EntityCode { get; set; } = "";

    public static QuestItemStackData? FromItemStack(ItemStack? stack)
    {
        if (stack == null || stack.Collectible == null || stack.StackSize <= 0) return null;

        return new QuestItemStackData
        {
            Code = stack.Collectible.Code?.ToString() ?? "",
            Class = stack.Class.ToString(),
            StackSize = stack.StackSize,
            EntityCode = FindCreatureCode(stack)
        };
    }

    public string GetQuestTargetCode()
    {
        return !string.IsNullOrWhiteSpace(EntityCode) ? EntityCode : Code;
    }

    private static string FindCreatureCode(ItemStack stack)
    {
        string direct = stack.Collectible?.Code?.ToString() ?? "";
        if (LooksLikeCreatureCode(direct)) return direct;

        string fromAttributes = FindCreatureCodeInTree(stack.Attributes, 0);
        return fromAttributes ?? "";
    }

    private static string? FindCreatureCodeInTree(ITreeAttribute? tree, int depth)
    {
        if (tree == null || depth > 5) return null;

        foreach (KeyValuePair<string, IAttribute> entry in tree)
        {
            object? value = entry.Value?.GetValue();

            if (value is string text && LooksLikeCreatureCode(text)) return text;
            if (value is AssetLocation asset && LooksLikeCreatureCode(asset.ToString())) return asset.ToString();
            if (value is ITreeAttribute child)
            {
                string? found = FindCreatureCodeInTree(child, depth + 1);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }
        }

        return null;
    }

    private static bool LooksLikeCreatureCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        code = code.Trim();
        return code.StartsWith("game:creature", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith("creature", StringComparison.OrdinalIgnoreCase);
    }

    public ItemStack? ToItemStack(ICoreAPI api)
    {
        if (string.IsNullOrWhiteSpace(Code)) return null;

        AssetLocation code = new AssetLocation(Code);
        EnumItemClass itemClass = ParseClass(Class);

        CollectibleObject? collectible = itemClass == EnumItemClass.Block
            ? api.World.GetBlock(code)
            : api.World.GetItem(code);

        if (collectible == null) return null;

        return new ItemStack(collectible, Math.Max(1, StackSize));
    }

    private static EnumItemClass ParseClass(string? value)
    {
        if (Enum.TryParse(value, true, out EnumItemClass parsed)) return parsed;
        return EnumItemClass.Item;
    }
}
