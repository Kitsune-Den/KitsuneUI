using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// "Show the whole backpack": instead of scrolling, the backpack grid is re-laid with
/// more columns and scaled down so every slot fits the window's visible area. The
/// column count is the one that gives the biggest slots. Turned off, the grid goes back
/// to its own columns, rows and size. Works with mods that change the bag (it reads the
/// grid's own slot count, e.g. KitsuneBackpack's 8 x 17).
/// </summary>
public static class KitsuneWholeBackpack
{
    sealed class Original
    {
        public int Columns, Rows;
    }

    static readonly ConditionalWeakTable<XUiV_Grid, Original> originals = new ConditionalWeakTable<XUiV_Grid, Original>();

    /// <summary>Called by the settings walk for every grid; acts on the backpack's.</summary>
    public static void Apply(XUiV_Grid grid, XUiController controller, bool whole)
    {
        if (grid.ID != "inventory" || !InBackpack(controller))
        {
            return;
        }
        Original o = originals.GetValue(grid, g => new Original { Columns = g.Columns, Rows = g.Rows });
        Transform t = grid.UiTransform;
        // Nothing left to scroll, so the scroll bar (a sibling of the scroll view) goes too.
        if (controller.Parent?.Parent?.GetChildById("scrollbar")?.ViewComponent is XUiView bar)
        {
            bar.IsVisible = !whole;
        }
        if (!whole)
        {
            grid.Columns = o.Columns;
            grid.Rows = o.Rows;
            if (t != null && t.localScale != Vector3.one)
            {
                t.localScale = Vector3.one;
                ResetScroll(t);
            }
            return;
        }

        int slots = o.Columns * o.Rows;
        int cellW = Mathf.Max(1, grid.CellWidth);
        int cellH = Mathf.Max(1, grid.CellHeight);
        // The scroll view the grid sits in is the visible area.
        Vector2i area = controller.Parent?.ViewComponent?.Size ?? new Vector2i(587, 359);
        float w = area.x > 0 ? area.x : 587f;
        float h = area.y > 0 ? area.y : 359f;

        int bestCols = o.Columns;
        float bestScale = 0f;
        for (int cols = o.Columns; cols <= slots; cols++)
        {
            int rows = (slots + cols - 1) / cols;
            float scale = Mathf.Min(1f, Mathf.Min(w / (cols * cellW), h / (rows * cellH)));
            if (scale > bestScale + 0.0001f)
            {
                bestScale = scale;
                bestCols = cols;
            }
        }
        grid.Columns = bestCols;
        grid.Rows = (slots + bestCols - 1) / bestCols;
        if (t != null)
        {
            t.localScale = new Vector3(bestScale, bestScale, 1f);
            ResetScroll(t);
        }
    }

    // Back to the top, so a bag that was scrolled down doesn't stay shifted once it all fits.
    static void ResetScroll(Transform t)
    {
        UIScrollView scroll = t.GetComponentInParent<UIScrollView>();
        if (scroll != null)
        {
            scroll.ResetPosition();
        }
    }

    static bool InBackpack(XUiController c)
    {
        for (XUiController p = c; p != null; p = p.Parent)
        {
            if (p.ViewComponent is XUiV_Window w)
            {
                return w.ID == "windowBackpack";
            }
        }
        return false;
    }
}
