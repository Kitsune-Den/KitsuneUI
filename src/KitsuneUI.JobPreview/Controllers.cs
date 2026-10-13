using UnityEngine;

/// <summary>
/// Entry point for KitsuneUI_JobPreview. The work is done by the two XUi controllers
/// below, which the add-on's XML places in the dialog rows and the job offer window
/// as controller="KitsuneJobTile, KitsuneUI_JobPreview" (XUi adds the XUiC_ prefix).
/// </summary>
public class KitsuneUIJobPreviewMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        Log.Out("[KitsuneUI_JobPreview] Loaded - trader jobs show as tiles with location previews");
    }
}

/// <summary>
/// One tile in the trader's dialog strip. Lives inside a dialog response row and reads
/// that row's response: a job gets its picture, place name, tier badge and short line;
/// anything else (Let's trade, Nevermind...) shows as a text-only tile.
/// </summary>
public class XUiC_KitsuneJobTile : XUiController
{
    XUiC_DialogResponseEntry entry;
    DialogResponse shownResponse;
    bool shownHot;
    bool shownEnabled;
    JobInfo info = new JobInfo();
    string text = "";

    public override void Init()
    {
        base.Init();
        for (XUiController p = Parent; p != null; p = p.Parent)
        {
            if (p is XUiC_DialogResponseEntry e)
            {
                entry = e;
                break;
            }
        }
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        if (entry == null)
        {
            return;
        }
        DialogResponse response = entry.CurrentResponse;
        bool hot = entry.IsHovered || entry.Selected;
        bool enabled = entry.HasRequirement;
        if (response == shownResponse && hot == shownHot && enabled == shownEnabled)
        {
            return;
        }
        if (response != shownResponse)
        {
            info = response is DialogResponseQuest q && q.Quest != null ? JobInfo.From(q.Quest, withObjectives: false) : new JobInfo();
            text = "";
            entry.GetBindingValueInternal(ref text, "response");
        }
        shownResponse = response;
        shownHot = hot;
        shownEnabled = enabled;
        RefreshBindings();
    }

    bool IsJob => shownResponse is DialogResponseQuest q && q.Quest != null;

    public override bool GetBindingValueInternal(ref string _value, string _bindingName)
    {
        switch (_bindingName)
        {
            case "kt_show":
                _value = (shownResponse != null).ToString();
                return true;
            case "kt_isjob":
                _value = (shownResponse != null && IsJob).ToString();
                return true;
            case "kt_notjob":
                _value = (shownResponse != null && !IsJob).ToString();
                return true;
            case "kt_hasimage":
                _value = (shownResponse != null && IsJob && info.Image.Length > 0).ToString();
                return true;
            case "kt_image":
                _value = info.Image;
                return true;
            case "kt_name":
                _value = info.Name;
                return true;
            case "kt_tier":
                _value = info.Tier;
                return true;
            case "kt_text":
                _value = IsJob && info.ActionShort.Length > 0 ? info.ActionShort : text;
                return true;
            case "kt_distance":
                _value = info.Distance.Length > 0 ? (info.Distance + " " + info.Direction).Trim() : "";
                return true;
            case "kt_hasdistance":
                _value = (shownResponse != null && IsJob && info.Distance.Length > 0).ToString();
                return true;
            case "kt_hot":
                _value = shownHot.ToString();
                return true;
            case "kt_textcolor":
                _value = !shownEnabled ? "160,168,196,255" : (shownHot ? "242,245,252,255" : "201,211,238,255");
                return true;
            default:
                return base.GetBindingValueInternal(ref _value, _bindingName);
        }
    }
}

/// <summary>
/// Sits on the dialog's tile grid and keeps the strip centred under the speaker:
/// the grid is laid out from the left with room for 10 tiles, so with fewer options
/// the whole window (name and tiles) is shifted right by half the unused width.
/// </summary>
public class XUiC_KitsuneDialogStrip : XUiController
{
    public int CellWidth = 152;
    public int Slots = 10;

    XUiView window;
    Vector2i basePosition;
    int shownCount = -1;

    public override void Update(float _dt)
    {
        base.Update(_dt);
        if (window == null)
        {
            for (XUiController p = Parent; p != null; p = p.Parent)
            {
                if (p.ViewComponent is XUiV_Window)
                {
                    window = p.ViewComponent;
                    basePosition = window.Position;
                    break;
                }
            }
            if (window == null)
            {
                return;
            }
        }

        int count = 0;
        foreach (XUiController child in GetChildrenByType<XUiC_DialogResponseEntry>())
        {
            if (((XUiC_DialogResponseEntry)child).CurrentResponse != null)
            {
                count++;
            }
        }
        if (count == shownCount)
        {
            return;
        }
        shownCount = count;
        int offset = (Slots - Mathf.Clamp(count, 1, Slots)) * CellWidth / 2;
        window.Position = new Vector2i(basePosition.x + offset, basePosition.y);
    }

    public override bool ParseAttribute(string _name, string _value)
    {
        switch (_name)
        {
            case "cell_width_px":
                CellWidth = int.Parse(_value);
                return true;
            case "slots":
                Slots = int.Parse(_value);
                return true;
            default:
                return base.ParseAttribute(_name, _value);
        }
    }

    public override void OnClose()
    {
        base.OnClose();
        shownCount = -1;   // re-centre on the next open
    }
}

/// <summary>
/// The location panel in the job offer window: picture, place name, tier, distance,
/// direction, biome and objectives for the job being offered.
/// </summary>
public class XUiC_KitsuneJobOffer : XUiController
{
    XUiC_QuestOfferWindow window;
    Quest shownQuest;
    JobInfo info = new JobInfo();

    public override void Init()
    {
        base.Init();
        for (XUiController p = Parent; p != null; p = p.Parent)
        {
            if (p is XUiC_QuestOfferWindow w)
            {
                window = w;
                break;
            }
        }
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        Quest quest = window?.Quest;
        if (quest == shownQuest)
        {
            return;
        }
        shownQuest = quest;
        info = JobInfo.From(quest, withObjectives: true);
        RefreshBindings();
    }

    public override bool GetBindingValueInternal(ref string _value, string _bindingName)
    {
        switch (_bindingName)
        {
            case "kj_hasimage":
                _value = (info.Image.Length > 0).ToString();
                return true;
            case "kj_image":
                _value = info.Image;
                return true;
            case "kj_name":
                _value = info.Name;
                return true;
            case "kj_tier":
                _value = info.Tier;
                return true;
            case "kj_distance":
                _value = info.Distance.Length > 0 ? (info.Distance + " " + info.Direction).Trim() : "";
                return true;
            case "kj_biome":
                _value = info.Biome;
                return true;
            case "kj_objectives":
                _value = info.Objectives;
                return true;
            case "kj_hasobjectives":
                _value = (info.Objectives.Length > 0).ToString();
                return true;
            default:
                return base.GetBindingValueInternal(ref _value, _bindingName);
        }
    }
}
