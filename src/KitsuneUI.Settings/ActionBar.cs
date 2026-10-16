using HarmonyLib;
using UnityEngine;

/// <summary>
/// The action bar (Settings > HUD), a cast bar like WoW's: while you reload, eat, drink,
/// or use a bandage or other medicine on yourself or someone else, a glass strip above
/// the toolbelt shows the item, what you're doing, the time left and a bar filling up.
/// It hides the moment the action ends or is cancelled. It has its own move-mode handle.
///
/// Eating and medicine time themselves in the item action (ItemActionEat.PercentDone,
/// ItemActionUseOther's Delay). Reloads are animations, so KitsuneReloadProgress
/// records how far through the reload state the local player's animator is.
/// </summary>
public class XUiC_KitsuneActionBar : XUiController
{
    bool active;
    string icon = "", tint = "255,255,255,255", name = "", time = "";
    float fill;

    public override void Update(float _dt)
    {
        base.Update(_dt);
        active = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.ActionBar) && Read();
        if (ViewComponent.IsVisible != active)
        {
            ViewComponent.IsVisible = active;
        }
        if (active)
        {
            RefreshBindings();
        }
    }

    bool Read()
    {
        EntityPlayerLocal player = xui?.playerUI?.entityPlayer;
        ItemInventoryData held = player?.inventory?.holdingItemData;
        ItemClass item = player?.inventory?.holdingItem;
        if (held?.actionData == null || item?.Actions == null || player.IsDead())
        {
            return false;
        }
        float progress = -1f, length = 0f;
        string doing = null;

        if (held.actionData.Count > 0 && held.actionData[0] is ItemActionRanged.ItemActionDataRanged ranged
            && (ranged.isReloading || ranged.isWeaponReloading) && !ranged.isReloadCancelled
            && Time.time - KitsuneReloadProgress.Seen < 0.25f)
        {
            progress = KitsuneReloadProgress.Progress;
            length = KitsuneReloadProgress.Length;
            doing = Localization.Get("kuActReloading");
        }
        for (int i = 0; doing == null && i < held.actionData.Count && i < item.Actions.Length; i++)
        {
            if (item.Actions[i] is ItemActionEat eat && held.actionData[i] is ItemActionEat.MyInventoryData eating && eating.bEatingStarted)
            {
                length = AnimationDelayData.AnimationDelay[item.HoldType.Value].RayCast;
                progress = eat.PercentDone(eating);
                doing = item.GetLocalizedItemName();
            }
            // Using it on someone else: the game marks the action started even with nobody in
            // front of you (it just heals no one), so only show it with a living target.
            else if (item.Actions[i] is ItemActionUseOther other && held.actionData[i] is ItemActionUseOther.FeedInventoryData feeding
                && feeding.bFeedingStarted && feeding.TargetEntity != null && feeding.TargetEntity.IsAlive()
                && other.Delay > 0f && Time.time - feeding.lastUseTime < other.Delay)
            {
                length = other.Delay;
                progress = (Time.time - feeding.lastUseTime) / other.Delay;
                doing = feeding.TargetEntity is EntityPlayer target
                    ? string.Format(Localization.Get("kuActOnTarget"), item.GetLocalizedItemName(), target.PlayerDisplayName)
                    : item.GetLocalizedItemName();
            }
        }
        if (doing == null || progress < 0f || progress >= 1f)
        {
            return false;
        }
        icon = item.GetIconName();
        Color32 c = item.GetIconTint(held.itemValue);
        tint = c.r + "," + c.g + "," + c.b + "," + c.a;
        name = doing;
        fill = Mathf.Clamp01(progress);
        time = length > 0f ? (length * (1f - fill)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s" : "";
        return true;
    }

    public override bool GetBindingValueInternal(ref string _value, string _bindingName)
    {
        switch (_bindingName)
        {
            case "kuacticon":
                _value = icon;
                return true;
            case "kuacttint":
                _value = tint;
                return true;
            case "kuactname":
                _value = name;
                return true;
            case "kuacttime":
                _value = time;
                return true;
            case "kuactfill":
                _value = fill.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                return true;
        }
        return base.GetBindingValueInternal(ref _value, _bindingName);
    }
}

/// <summary>How far through the reload animation the local player is, for the action bar.</summary>
[HarmonyPatch(typeof(AnimatorRangedReloadState), nameof(AnimatorRangedReloadState.OnStateUpdate),
    new[] { typeof(Animator), typeof(AnimatorStateInfo), typeof(int) })]
public static class KitsuneReloadProgress
{
    public static float Progress, Length, Seen = -10f;

    static void Postfix(AnimatorRangedReloadState __instance, AnimatorStateInfo stateInfo)
    {
        ItemActionRanged.ItemActionDataRanged data = __instance.actionData;
        if (data?.invData?.holdingEntity is EntityPlayerLocal && !data.isReloadCancelled)
        {
            // Reloads that load round by round loop the state: show each round's progress.
            float t = stateInfo.normalizedTime;
            Progress = t > 1f ? t - Mathf.Floor(t) : Mathf.Max(0f, t);
            Length = stateInfo.length;
            Seen = Time.time;
        }
    }
}
