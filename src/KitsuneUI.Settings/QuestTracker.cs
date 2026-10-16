using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Quest tracker extras (Settings > Windows), one per objective line in the tracker:
/// a thin progress bar under objectives that count something ("3/10" kills, items,
/// challenge steps), and an arrow beside "go to" objectives pointing the way to the
/// target from where you're facing. Lives in a child rect of the game's objective
/// entry and reads that entry's objective; finished objectives show neither.
/// </summary>
public class XUiC_KitsuneQuestObjective : XUiController
{
    const float RefreshSeconds = 0.25f;
    static readonly Regex Fraction = new Regex(@"(\d+)\s*/\s*(\d+)");

    XUiC_QuestTrackerObjectiveEntry entry;
    XUiView arrow;
    float sinceRefresh = RefreshSeconds;
    bool bar, pointer;
    float fill;
    NavObject target;

    public override void Init()
    {
        base.Init();
        entry = GetParentByType<XUiC_QuestTrackerObjectiveEntry>();
        arrow = GetChildById("kuObjArrow")?.ViewComponent;
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        sinceRefresh += _dt;
        if (sinceRefresh >= RefreshSeconds)
        {
            sinceRefresh = 0f;
            bool wasBar = bar, wasPointer = pointer;
            float wasFill = fill;
            Read();
            if (bar != wasBar || pointer != wasPointer || !Mathf.Approximately(fill, wasFill))
            {
                RefreshBindings();
            }
        }
        if (pointer && target != null && arrow?.UiTransform != null)
        {
            EntityPlayerLocal player = xui?.playerUI?.entityPlayer;
            if (player != null)
            {
                Vector3 to = target.GetPosition() + Origin.position - player.position;
                float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                arrow.UiTransform.localEulerAngles = new Vector3(0f, 0f, player.rotation.y - bearing);
            }
        }
    }

    void Read()
    {
        bar = pointer = false;
        target = null;
        if (entry == null || !KitsuneSettingsFile.GetBool(KitsuneSettingsFile.QuestBars))
        {
            return;
        }
        if (entry.ChallengeObjective is Challenges.BaseChallengeObjective challenge)
        {
            if (!challenge.Complete && challenge.MaxCount > 1)
            {
                bar = true;
                fill = Mathf.Clamp01(challenge.FillAmount);
            }
            return;
        }
        BaseObjective objective = entry.QuestObjective;
        Quest quest = objective?.OwnerQuest;
        if (objective == null || quest == null || !quest.Active || objective.Complete)
        {
            return;
        }
        bool current = objective.Phase == 0 || objective.Phase == quest.CurrentPhase;
        if (objective.ObjectiveValueType == BaseObjective.ObjectiveValueTypes.Distance)
        {
            target = objective.NavObject;
            pointer = current && target != null;
            return;
        }
        if (objective.ObjectiveValueType == BaseObjective.ObjectiveValueTypes.Boolean)
        {
            return;
        }
        Match m = Fraction.Match(objective.StatusText ?? "");
        if (m.Success && int.TryParse(m.Groups[1].Value, out int have) && int.TryParse(m.Groups[2].Value, out int need) && need > 1)
        {
            bar = current;
            fill = Mathf.Clamp01((float)have / need);
        }
    }

    public override bool GetBindingValueInternal(ref string _value, string _bindingName)
    {
        switch (_bindingName)
        {
            case "kuobjbar":
                _value = bar.ToString();
                return true;
            case "kuobjfill":
                _value = fill.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                return true;
            case "kuobjarrow":
                _value = pointer.ToString();
                return true;
        }
        return base.GetBindingValueInternal(ref _value, _bindingName);
    }
}
