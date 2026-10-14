using UnityEngine;

/// <summary>
/// "Horde night countdown": a line under the compass's day and time that counts down
/// to the next blood moon ("Horde night in 3 days", "tomorrow", "tonight") and turns
/// red on the day and while it lasts. It reads the same blood moon day the game uses
/// to turn the day number red, so it follows the server's settings. Hidden when blood
/// moons are off.
/// </summary>
public class XUiC_KitsuneHordeCountdown : XUiController
{
    const float RefreshSeconds = 1f;

    XUiV_Label label;
    float sinceRefresh = RefreshSeconds;

    public override void Init()
    {
        base.Init();
        label = GetChildById("kuHordeText")?.ViewComponent as XUiV_Label;
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        sinceRefresh += _dt;
        if (sinceRefresh < RefreshSeconds)
        {
            return;
        }
        sinceRefresh = 0f;
        string text = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.HordeCountdown) ? Text() : null;
        bool show = !string.IsNullOrEmpty(text);
        if (ViewComponent.IsVisible != show)
        {
            ViewComponent.IsVisible = show;
        }
        if (show && label != null && label.Text != text)
        {
            label.Text = text;
        }
    }

    static string Text()
    {
        World world = GameManager.Instance?.World;
        if (world == null || !XUi.IsGameRunning())
        {
            return null;
        }
        int bloodMoonDay = GameStats.GetInt(EnumGameStats.BloodMoonDay);
        if (bloodMoonDay <= 0)
        {
            return null;
        }
        (int day, int hour, int _) = GameUtils.WorldTimeToElements(world.worldTime);
        if (GameUtils.IsBloodMoonTime((world.DuskHour, world.DawnHour), hour, bloodMoonDay, day))
        {
            return "[FF5A4E]" + Localization.Get("kuHordeNow") + "[-]";
        }
        int days = bloodMoonDay - day;
        if (days < 0)
        {
            return null;
        }
        if (days == 0)
        {
            return "[FF5A4E]" + Localization.Get("kuHordeTonight") + "[-]";
        }
        if (days == 1)
        {
            return "[FFB26B]" + Localization.Get("kuHordeTomorrow") + "[-]";
        }
        return string.Format(Localization.Get("kuHordeDays"), days);
    }
}

/// <summary>
/// "Crafting progress on the HUD": while the player's own crafting queue (the backpack
/// crafting window, which keeps going with the window closed) has something in it, a
/// small glass card shows the item, how many are left, the time left for all of them
/// and a bar for the one being made. Workstations are not covered: their queues live
/// in their blocks. It has its own move-mode handle.
/// </summary>
public class XUiC_KitsuneCraftingHud : XUiController
{
    const float RefreshSeconds = 0.25f;

    float sinceRefresh = RefreshSeconds;
    bool active;
    string icon = "", tint = "255,255,255,255", name = "", count = "", time = "";
    float fill;

    public override void Update(float _dt)
    {
        base.Update(_dt);
        sinceRefresh += _dt;
        if (sinceRefresh < RefreshSeconds)
        {
            return;
        }
        sinceRefresh = 0f;
        active = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.CraftingHud) && Read();
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
        XUiC_CraftingQueue queue = xui?.FindWindowGroupByName("crafting")?.GetChildByType<XUiC_CraftingQueue>();
        XUiC_RecipeStack[] stacks = queue?.GetRecipesToCraft();
        if (stacks == null)
        {
            return false;
        }
        XUiC_RecipeStack current = null;
        float total = 0f;
        int items = 0;
        foreach (XUiC_RecipeStack stack in stacks)
        {
            if (stack == null || stack.GetRecipe() == null || stack.GetRecipeCount() <= 0)
            {
                continue;
            }
            int n = stack.GetRecipeCount();
            float left = stack.GetRecipeCraftingTimeLeft();
            float one = stack.GetOneItemCraftTime();
            total += left + Mathf.Max(0, n - 1) * one;
            items += n;
            if (current == null || (stack.IsCrafting && !current.IsCrafting))
            {
                current = stack;
            }
        }
        if (current == null)
        {
            return false;
        }
        ItemClass item = current.GetRecipe().GetOutputItemClass();
        if (item == null)
        {
            return false;
        }
        icon = item.GetIconName();
        Color32 c = item.GetIconTint();
        tint = c.r + "," + c.g + "," + c.b + "," + c.a;
        name = item.GetLocalizedItemName();
        count = items > 1 ? "x" + items : "";
        time = Format(total);
        float oneTime = current.GetOneItemCraftTime();
        fill = oneTime > 0f ? Mathf.Clamp01(1f - current.GetRecipeCraftingTimeLeft() / oneTime) : 0f;
        return true;
    }

    static string Format(float seconds)
    {
        int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    public override bool GetBindingValueInternal(ref string _value, string _bindingName)
    {
        switch (_bindingName)
        {
            case "kucrafticon":
                _value = icon;
                return true;
            case "kucrafttint":
                _value = tint;
                return true;
            case "kucraftname":
                _value = name;
                return true;
            case "kucraftcount":
                _value = count;
                return true;
            case "kucrafttime":
                _value = time;
                return true;
            case "kucraftfill":
                _value = fill.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                return true;
        }
        return base.GetBindingValueInternal(ref _value, _bindingName);
    }
}
