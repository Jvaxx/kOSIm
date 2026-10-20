using System;
using System.Collections.Generic;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using UnityEngine;

namespace kOS.Screen
{
    /// <summary>
    /// A GUI widget that lets the user pick a string from a list of strings.
    /// It creates the widget and leaves it showing while the rest of the C# code
    /// continues on.  Then when the user picks one of the strings
    /// the ChangeAction delegate is called to let the C# code know the pick
    /// has happened.
    /// (Not to be confused with the kerbscript-capable popup dialog.  This is
    /// for use with kOS's own C# code.)
    /// <para>
    /// Phase 4, step 5: a flat window (<see cref="FlatWindowLayout"/>) beside the window that
    /// summoned it (the toolbar panel) and kept on screen: a header with the title and a close
    /// button, the subtitle, then the list, scrolled to the current entry, which is shown in
    /// the accent color.
    /// </para>
    /// </summary>
    public class ListPickerDialog : MonoBehaviour
    {
        /// <summary>
        /// Your callback that this class will invoke upon a selection from the list.
        /// If you make it return true, you are telling ListPickerDialog that you allow
        /// the selection and it should highlight the selection.  If you make this
        /// return false, then you are forbidding the selection and the list
        /// picker will not highlight the selected thing.
        /// </summary>
        public delegate bool ChangeAction(string pick);

        /// <summary>
        /// Your callback to be invoked when the user closes the list box.
        /// </summary>
        public delegate void CloseAction();

        /// <summary>At most this many rows show; more scroll.</summary>
        private const int VISIBLE_ROWS = 12;

        private List<string> choices;
        private string current;
        private string title;
        private string subTitle;
        private Rect anchor;
        private Rect windowRect;
        private MouseBlocking.Area mouseArea;
        private bool running;
        private bool closed; // Destroy requested; the component goes at the end of the frame
        private ChangeAction callWhenChanged;
        private CloseAction callWhenClosed;
        private FlatWindowLayout layout;
        private float listWidth = -1; // measured at the first draw, and again when the UI scale changes
        private float scroll;
        private bool scrolledToCurrent;
        private readonly FlatScrollBar scrollBar = new FlatScrollBar();

        ListPickerDialog()
        {
            running = false;
        }

        /// <summary>
        /// Starts the picker at (<paramref name="leftX"/>, <paramref name="topY"/>), as upstream's
        /// callers did; it is placed beside that point and kept on screen.
        /// </summary>
        /// <param name="minWidth">ignored: the width follows the longest choice</param>
        public void Summon(
            float leftX,
            float topY,
            float minWidth,
            string title,
            string subTitle,
            string current,
            IEnumerable<string> choices,
            ChangeAction callWhenChanged,
            CloseAction callWhenClosed)
        {
            Summon(new Rect(leftX, topY, 0, 0), title, subTitle, current, choices, callWhenChanged, callWhenClosed);
        }

        /// <summary>
        /// Starts the instance of the <see cref="kOS.Screen.ListPickerDialog"/> class.
        /// Because Monobehaviour doesn't like setting up things in the constructor, you call this
        /// after the constructor:
        /// </summary>
        /// <param name="besideRect">the window it opens beside (right of it, else left, below, above)</param>
        /// <param name="title">Title text.</param>
        /// <param name="subTitle">A note under the title, or null.</param>
        /// <param name="current">Current string value.</param>
        /// <param name="choices">Choices.</param>
        /// <param name="callWhenChanged">Your Callback invoked when a new pick has been made.</param>
        /// <param name="callWhenClosed">Your Callback invoked when the user closes the dialog box.</param>
        public void Summon(
            Rect besideRect,
            string title,
            string subTitle,
            string current,
            IEnumerable<string> choices,
            ChangeAction callWhenChanged,
            CloseAction callWhenClosed)
        {
            this.choices = new List<string>(choices);
            this.current = current;
            this.title = title;
            this.subTitle = subTitle;
            this.callWhenChanged = callWhenChanged;
            this.callWhenClosed = callWhenClosed;
            anchor = besideRect;
            listWidth = -1;
            scroll = 0;
            scrolledToCurrent = false;
            running = true;
        }

        public void Awake()
        {
            running = false;
        }

        /// <summary>
        /// Close it without a pick: it is destroyed (at the end of the frame), and
        /// <see cref="CloseAction"/> is not called. Safe to call more than once. Before L-01 this only
        /// hid it, so each picker closed this way stayed on its GameObject, its mouse area still
        /// registered.
        /// </summary>
        public void Close()
        {
            running = false;
            if (closed)
                return;
            closed = true;
            Destroy(this);
        }

        public void OnDestroy()
        {
            KOSMouseBlocking.Unregister(ref mouseArea);
        }

        private FlatWindowLayout Layout
        {
            get
            {
                float scale = TerminalChromeLayout.ClampScale(KOSStyle.UiScale);
                if (layout == null || layout.Scale != scale)
                {
                    layout = new FlatWindowLayout(scale);
                    listWidth = -1;
                }
                return layout;
            }
        }

        private float SubTitleHeight(FlatWindowLayout flat, float width)
        {
            if (string.IsNullOrEmpty(subTitle)) return 0;
            return Mathf.Ceil(KOSStyle.MeasureHeight(KOSStyle.Paragraph(flat.SmallFontSize), subTitle, width)) + flat.Gap;
        }

        public void OnGUI()
        {
            if (!running)
                return;

            FlatWindowLayout flat = Layout;
            float screenWidth = UnityEngine.Screen.width, screenHeight = UnityEngine.Screen.height;
            if (listWidth < 0)
            {
                GUIStyle row = KOSStyle.TitleLabel(flat.FontSize);
                float longest = KOSStyle.Measure(KOSStyle.TitleLabel(flat.FontSize), title) + flat.IconButtonWidth;
                foreach (string choice in choices)
                    longest = Math.Max(longest, KOSStyle.Measure(row, choice));
                listWidth = longest + 2 * flat.Padding + flat.ScrollBarWidth + flat.Gap;
            }
            float width = Math.Max(flat.DialogWidth * 0.75f, listWidth + 2 * flat.Padding);
            width = Mathf.Round(Math.Min(width, Math.Max(flat.DialogWidth * 0.75f, screenWidth / 2f)));
            float bodyWidth = width - 2 * flat.Padding;
            float body = SubTitleHeight(flat, bodyWidth) + flat.RowHeight * Math.Max(1, Math.Min(VISIBLE_ROWS, choices.Count));
            float height = flat.WindowHeight(body, FlatWindowBottom.None);
            if (screenHeight > 0) height = Math.Min(height, screenHeight);
            ScreenRect placed = WindowPlacement.Beside(new ScreenRect(anchor.x, anchor.y, anchor.width, anchor.height), width, height, screenWidth, screenHeight);
            windowRect = new Rect(placed.X, placed.Y, placed.Width, placed.Height);

            int id = title.GetHashCode();
            Rect drawn = GUI.Window(id, windowRect, DrawInnards, GUIContent.none, GUIStyle.none);
            GUI.BringWindowToFront(id);
            KOSMouseBlocking.Show(ref mouseArea, "ListPickerDialog", drawn);

            // This is meant to be a one-off instance.  Once it's been used to make one pick, it's gone:
            if (!running)
                Close();
        }

        private void DrawInnards(int windowId)
        {
            FlatWindowLayout flat = Layout;
            float width = windowRect.width, height = windowRect.height;
            ScreenRect close = flat.CloseButton(width);
            KOSStyle.FlatWindow(flat, width, height, title, close.X);
            bool closeButtonPressed = KOSStyle.IconButton(ToRect(close), KOSStyle.Icon.Close, Math.Max(6, (int)Math.Round(flat.HeaderHeight * 0.6)));

            Rect body = ToRect(flat.Body(width, height, FlatWindowBottom.None));
            float subTitleHeight = SubTitleHeight(flat, body.width);
            if (subTitleHeight > 0)
                KOSStyle.Text(new Rect(body.x, body.y, body.width, subTitleHeight - flat.Gap), subTitle, KOSStyle.Paragraph(flat.SmallFontSize), KOSStyle.DimText);
            var view = new Rect(body.x, body.y + subTitleHeight, body.width, Math.Max(0, body.height - subTitleHeight));

            string picked = DrawList(flat, view);
            KOSStyle.FlatWindowFrame(flat, width, height, false);

            if (picked != null && !picked.Equals(current))
            {
                // Notify the caller of the change, and if they accept it, commit it so
                // the widget will highlight the new pick (else the highlight won't move).
                if (callWhenChanged(picked))
                {
                    current = picked;
                    running = false; // close on successful pick.
                    callWhenClosed();
                }
            }
            if (closeButtonPressed && running)
            {
                running = false;
                callWhenClosed();
            }
        }

        /// <summary>The choices in rows, scrolled in <paramref name="view"/>.</summary>
        /// <returns>the choice clicked in this event, or null</returns>
        private string DrawList(FlatWindowLayout flat, Rect view)
        {
            float rowHeight = flat.RowHeight;
            float contentHeight = rowHeight * choices.Count;
            bool scrolls = contentHeight > view.height;
            if (!scrolledToCurrent)
            {
                int index = choices.IndexOf(current);
                if (index >= 0)
                    scroll = PixelScroll.Reveal(0, index * rowHeight, rowHeight, contentHeight, view.height);
                scrolledToCurrent = true;
            }
            scroll = PixelScroll.Clamp(scroll, contentHeight, view.height);

            Event e = Event.current;
            if (e.type == EventType.ScrollWheel && scrolls && view.Contains(e.mousePosition))
            {
                scroll = PixelScroll.Clamp(scroll + e.delta.y * rowHeight, contentHeight, view.height);
                e.Use();
            }

            bool mouseInView = view.Contains(e.mousePosition);
            float rowWidth = scrolls ? Math.Max(0, view.width - flat.ScrollBarWidth - flat.Gap) : view.width;
            string picked = null;
            GUI.BeginGroup(view);
            try
            {
                GUIStyle label = KOSStyle.Label(flat.FontSize), selected = KOSStyle.TitleLabel(flat.FontSize);
                int first = Math.Max(0, (int)(scroll / rowHeight));
                for (int i = first; i < choices.Count; ++i)
                {
                    var row = new Rect(0, i * rowHeight - scroll, rowWidth, rowHeight);
                    if (row.y >= view.height) break;
                    bool isCurrent = choices[i].Equals(current);
                    if (mouseInView && row.Contains(e.mousePosition))
                        KOSStyle.Fill(row, KOSStyle.Hover);
                    var text = new Rect(row.x + flat.Padding, row.y, Math.Max(0, row.width - 2 * flat.Padding), row.height);
                    GUIStyle style = isCurrent ? selected : label;
                    KOSStyle.Text(text, StatusStrip.Shorten(choices[i], text.width, measured => KOSStyle.Measure(style, measured), KOSStyle.Ellipsis),
                        style, isCurrent ? KOSStyle.Accent : KOSStyle.ChromeText);
                    if (GUI.Button(row, GUIContent.none, GUIStyle.none) && mouseInView)
                        picked = choices[i];
                }
            }
            finally
            {
                GUI.EndGroup();
            }
            if (scrolls)
                scroll = scrollBar.Draw(flat.ScrollTrack(new ScreenRect(view.x, view.y, view.width, view.height)), scroll, contentHeight, view.height, flat.MinimumThumb);
            else
                scrollBar.Release();
            return picked;
        }

        private static Rect ToRect(ScreenRect rect)
        {
            return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        }
    }
}
