using System.Reflection;
using System.Text.RegularExpressions;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;

namespace TheTinkererZhHans;

[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    public static void Initialize()
    {
        var harmony = new Harmony("TheTinkererZhHans");
        harmony.Patch(
            AccessTools.Method(typeof(LocString), "AddObj", [typeof(string), typeof(object)]),
            prefix: new HarmonyMethod(typeof(MainFile), nameof(LocalizeVariable)));
        PatchOptional(harmony, "TheTinkererMod.Models.Powers.BotLayoutManager", "AttachTooltip", nameof(LocalizeBotTooltip), false);
        PatchOptional(harmony, "TheTinkererMod.Models.Powers.ExcaliBotPower", "UpdateSpriteTooltip", nameof(LocalizeUpdatedBotTooltip), true);
        GD.Print("[TheTinkererZhHans] Simplified Chinese localization initialized.");
    }

    private static bool IsSimplifiedChinese => LocManager.Instance?.Language == "zhs";

    private static void PatchOptional(Harmony harmony, string typeName, string methodName, string patchName, bool postfix)
    {
        var type = AccessTools.TypeByName(typeName);
        var method = type is null ? null : AccessTools.Method(type, methodName);
        if (method is null)
        {
            GD.PushWarning($"[TheTinkererZhHans] Tooltip method unavailable: {typeName}.{methodName}");
            return;
        }
        var patch = new HarmonyMethod(typeof(MainFile), patchName);
        harmony.Patch(method, prefix: postfix ? null : patch, postfix: postfix ? patch : null);
    }

    private static void LocalizeVariable(LocString __instance, string name, ref object variable)
    {
        if (!IsSimplifiedChinese || !__instance.LocEntryKey.Contains("THETINKERERMOD-", StringComparison.Ordinal)
            || variable is not string text)
            return;
        variable = TranslateVariable(name, text);
    }

    internal static string TranslateVariable(string name, string text)
    {
        if (name == "Plural")
            return text switch
            {
                "time" or "times" => "次",
                "card" or "cards" or "additional card" or "additional cards" => "张牌",
                _ => text
            };
        if (name == "Count" && text == "an") return "1";
        if (name == "DrawText")
            return Regex.Replace(text, @"^Draw (\d+) cards?\.\n$", "抽$1张牌。\n");
        if (name == "UpgradeText")
            return Regex.Replace(text, @"^\nUpgrade it (\d+) times?\.$", "\n将其升级$1次。");
        if (name != "Text") return text;

        if (text == "Your card upgrades are twice as effective.")
            return "你的卡牌升级效果翻倍。";
        text = Regex.Replace(text, @"^Your card upgrades are (\d+)x as effective\.$", "你的卡牌升级效果变为$1倍。");
        text = Regex.Replace(text, @"^Whenever Oil ignites on an enemy, Power a Bot randomly (\d+) times?\.$", "每当敌人身上的燃油被点燃时，随机为一个机器人充能$1次。");
        text = Regex.Replace(text, @"^Whenever you Power a Bot, Power it (\d+) additional times?\.$", "每当你为机器人充能时，额外为其充能$1次。");
        text = Regex.Replace(text, @"^At the start of your turn, Upgrade (\d+) random cards? in your hand\.$", "你的回合开始时，随机升级$1张手牌。");
        text = Regex.Replace(text, @"^At the start of your turn, add a Spare Part(\+\d+)? to your hand\.$", "你的回合开始时，将一张备用零件$1加入手牌。");
        return text;
    }

    private static void LocalizeBotTooltip(ref string __1)
    {
        if (IsSimplifiedChinese) __1 = TranslateTooltip(__1);
    }

    private static void LocalizeUpdatedBotTooltip(object __instance)
    {
        if (!IsSimplifiedChinese) return;
        var sprite = AccessTools.Field(__instance.GetType(), "_botSprite")?.GetValue(__instance) as Sprite2D;
        if (sprite is null || !GodotObject.IsInstanceValid(sprite)) return;
        foreach (var child in sprite.GetChildren())
            if (child is Control control && control.Name == "BotTooltipArea")
                control.TooltipText = TranslateTooltip(control.TooltipText);
    }

    internal static string TranslateTooltip(string text)
    {
        return text switch
        {
            "Better-Bot\nPowered: Upgrade up to 2 random cards in your hand." => "改良机器人\n充能时：随机升级至多2张手牌。",
            "Block-Bot\nPowered: Gain 6 Block." => "护盾机器人\n充能时：获得6点格挡。",
            "Bomb-Bot\nPowered: Nothing. Explodes when Overload is played." => "炸弹机器人\n充能时：无效果。打出过载时爆炸。",
            "Gunk-Bot\nPowered: Apply 8 Oil to a random enemy." => "油污机器人\n充能时：给予随机敌人8层燃油。",
            "Jank-Bot\nPowered: Gain 4 Block." => "破烂机器人\n充能时：获得4点格挡。",
            "Lift-Bot\nPowered: Gain 1 Strength." => "力量机器人\n充能时：获得1点力量。",
            "Power-Bot\nPowered: Gain 1 Energy." => "能源机器人\n充能时：获得1点能量。",
            "Shock-Bot\nPowered: Deal 8 damage to a random enemy." => "电击机器人\n充能时：对随机敌人造成8点伤害。",
            "Spike-Bot\nPowered: Gain 1 Thorns." => "尖刺机器人\n充能时：获得1层荆棘。",
            "Swift-Bot\nPowered: Draw 1 card." => "迅捷机器人\n充能时：抽1张牌。",
            _ => Regex.Replace(text, @"^Excali-Bot\nPowered: Deal (\d+) damage to ALL enemies, then increase this damage by 1\.$", "圣剑机器人\n充能时：对所有敌人造成$1点伤害，然后使此伤害增加1。")
        };
    }
}
