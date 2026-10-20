using kOS.Safe.Encapsulation;
using kOS.Safe.Persistence;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using EditorDocument = kOS.Safe.Screen.EditorDocument;
using System;
using System.Collections.Generic;
using UnityEngine;
using kOS.Safe.Utilities;
using kOS.Module;

namespace kOS.Screen
{
    /// <summary>
    /// A Unity window that contains the text editor for kOS inside it.  It should only be
    /// popped into existence when you feed it a file using the AttachTo call.
    /// <para>
    /// Phase 4, step 5: drawn in the flat style (<see cref="KOSStyle"/>, <see cref="FlatWindowLayout"/>):
    /// a header with the path and its buttons, the text in the terminal's font and colors, and a
    /// strip with the caret's line and column. It is a window of its own (EDIT-04): it opens
    /// next to its terminal where there is room, moves by its header, resizes by its edges and
    /// stays on screen; its place and size are kept while the terminal exists (not saved). The
    /// view follows the caret only when the caret moves or the text changes (EDIT-03).
    /// </para>
    /// </summary>
    public class KOSTextEditPopup : KOSManagedWindow
    {
        private const string TEXT_CONTROL = "kOSEditorText";
        private const string RELOAD_BUTTON_TEXT = "Reload";
        private const string SAVE_BUTTON_TEXT = "Save";
        private static readonly TimeSpan noticeDuration = TimeSpan.FromSeconds(2);

        private static readonly Color textColor = KOSStyle.ToColor(TerminalPalette.Text);
        private static readonly Color backgroundColor = KOSStyle.ToColor(TerminalPalette.Background);
        private static readonly Color caretColor = KOSStyle.ToColor(TerminalPalette.Cursor);
        private static readonly Color selectionColor = KOSStyle.ToColor(TerminalPalette.Text.WithAlpha(TerminalPalette.SELECTION_ALPHA));

        private int fontHeight = 12;
        private Font font;
        private string pickedFontName; // the TerminalFontName the font was looked up for
        private GUIStyle textStyle;
        private TermWindow term; // The terminal that this popup is attached to.
        private readonly EditorDocument document = new EditorDocument();
        private string contents
        {
            get { return document.Contents; }
            set { document.Contents = value; }
        }
        private bool isDirty { get { return document.IsDirty; } }
        // Without the keyboard focus (another window has it, or input is suspended) the editor is frozen, as
        // losing focus always did; it now also takes the terminal's control lock while focused (INPUT-01).
        private bool InteractionFrozen { get { return frozen || !HasKeyboard || DialogShown; } }
        private bool frozen;
        private DelegateDialog dialog;
        private bool DialogShown { get { return dialog != null && dialog.IsInvoked; } }

        private FlatWindowLayout layout;
        private bool placed; // the first Open put the window next to its terminal; later ones keep its place
        private float scroll; // pixels from the text's top
        private float viewHeight;
        private readonly EditorScrollFollow follow = new EditorScrollFollow();
        private readonly FlatScrollBar scrollBar = new FlatScrollBar();
        private int caretIndex;
        private bool caretHome; // a file was loaded: the caret goes to its start
        private bool focusTextPending; // opened by the user: the text takes the keyboard
        private string saveError; // shown in the strip until the next edit or save
        private string notice;
        private DateTime noticeEnd;

        private ResizeEdges resizeEdges = ResizeEdges.None;
        private Rect resizeStartRect;
        private Vector2 resizeStartMouse;

        public KOSTextEditPopup()
        {
            UniqueId = 100; // This is expected to be overridden, but Unity requires that
                            // KosTextEditPopup() be a constructor that takes zero arguments,
                            // so the real WindowId has to be set after construction.
        }

        public void Freeze(bool newVal)
        {
            frozen = newVal;
        }

        public void Awake()
        {
            WindowRect = new Rect(0, 0, 470, 280); // placed next to the terminal when first opened.
            dialog = gameObject.AddComponent<DelegateDialog>();
        }

        private FlatWindowLayout Layout
        {
            get
            {
                float scale = TerminalChromeLayout.ClampScale(KOSStyle.UiScale);
                if (layout == null || layout.Scale != scale)
                    layout = new FlatWindowLayout(scale);
                return layout;
            }
        }

        /// <summary>The terminal's font (its pick, then the step 3c fallbacks) at the terminal's character height.</summary>
        private void GetFontIfChanged()
        {
            int newHeight = GetConfigFontSize();
            string newName = SafeHouse.Config.TerminalFontName;
            if (textStyle != null && font != null && fontHeight == newHeight && string.Equals(pickedFontName, newName))
                return;
            fontHeight = newHeight;
            pickedFontName = newName;
            font = AssetManager.Instance.GetSystemFontByNameAndSize(TerminalFontChoice.NamesToTry(newName), fontHeight, false);

            textStyle = new GUIStyle
            {
                font = font,
                fontSize = fontHeight,
                wordWrap = true,
                richText = false, // a script is shown as it is
                clipping = TextClipping.Clip,
                padding = new RectOffset(4, 4, 2, 2),
                margin = new RectOffset(0, 0, 0, 0)
            };
            textStyle.normal.textColor = textColor;
            textStyle.hover.textColor = textColor;
            textStyle.focused.textColor = textColor;
            textStyle.active.textColor = textColor;
        }

        private int GetConfigFontSize()
        {
            // For a few moments upon first activating the text editor popup, the
            // TermWindow isn't attached yet so the term member is null.  Return a
            // dummy value at first but then correct it later the next time this is
            // queired after the term is attached:
            return (term != null ) ? term.GetFontSize() : 12;
        }

        public void AttachTo(TermWindow termWindow, Volume attachVolume, GlobalPath path)
        {
            term = termWindow;
            LoadContents(attachVolume, path);
        }

        public bool Contains(Vector2 posAbs)
        {
            return WindowRect.Contains(posAbs);
        }

        public override void Open(bool takeFocus)
        {
            // View operations never reset document state or resolve a modal transaction.
            if (!placed && term != null)
            {
                PlaceNextToTerminal();
                placed = true;
            }
            base.Open(takeFocus);
            BringToFront();
            focusTextPending = takeFocus;
        }

        /// <summary>The terminal's size, below it or beside it where it fits (EDIT-04).</summary>
        private void PlaceNextToTerminal()
        {
            FlatWindowLayout flat = Layout;
            Rect terminal = term.GetRect();
            float screenWidth = UnityEngine.Screen.width, screenHeight = UnityEngine.Screen.height;
            float width = Math.Min(Math.Max(flat.EditorMinimumWidth, terminal.width), screenWidth);
            float height = Math.Min(Math.Max(flat.EditorMinimumHeight, terminal.height), screenHeight);
            WindowRect = ToRect(WindowPlacement.BelowOrBeside(ToScreenRect(terminal), width, height, screenWidth, screenHeight));
        }

        public override void Close()
        {
            document.CancelOpen();
            if (dialog != null) dialog.Dismiss();
            resizeEdges = ResizeEdges.None;
            scrollBar.Release();
            base.Close();
        }

        public int GetUniqueId()
        {
            return UniqueId;
        }

        public void SetUniqueId(int newValue)
        {
            UniqueId = newValue;
        }

        public void Update()
        {
            // Only stay open as long as the attached terminal window stays open:
            if (IsOpen && (term == null || !(term.IsPowered)))
                IsOpen = false;
            UpdateLogic();
        }

        public void OnGUI()
        {
            if (!IsOpen) return;

            GetFontIfChanged();

            FollowResizeDrag();
            KeepOnScreen();
            WindowRect = GUI.Window(UniqueId, WindowRect, ProcessWindow, GUIContent.none, GUIStyle.none);
            KeepOnScreen(); // moved by its header
            BlockMouseOver(WindowRect);
            if (focusTextPending && !InteractionFrozen)
                GUI.FocusWindow(UniqueId); // the keys go to this window's text (see DrawText)
        }

        protected void ExitEditor()
        {
            if (isDirty)
                InvokeDirtySaveExitDialog();
            else
                Close();
        }

        public void SaveContents()
        {
            try
            {
                document.Save();
            }
            catch (Exception ex) when (DelegateDialog.CanDisplayFileError(ex))
            {
                saveError = "Save failed: " + ex.Message;
                term.Print("[File Save failed: " + ex.Message + "]");
                throw;
            }
            saveError = null;
            ShowNotice("Saved");
            term.Print("[Saved changes to " + document.ActivePath + "]");
        }

        /// <summary>Save from the button or Ctrl+S: a failure is shown in the strip (and on the terminal), not thrown out of OnGUI.</summary>
        private void TrySave()
        {
            try
            {
                SaveContents();
            }
            catch (Exception ex) when (DelegateDialog.CanDisplayFileError(ex))
            {
                // SaveContents reported it.
            }
        }

        private void ShowNotice(string text)
        {
            notice = text;
            noticeEnd = DateTime.Now + noticeDuration;
        }

        protected void ReloadContents()
        {
            RequestContents(document.ActiveVolume, document.ActivePath, true);
        }

        public void LoadContents(Volume vol, GlobalPath path)
        {
            RequestContents(vol, path, false);
        }

        private void RequestContents(Volume vol, GlobalPath path, bool reload)
        {
            // Do not replace the target or callbacks of an unresolved confirmation.
            if (dialog.IsInvoked) return;
            try
            {
                if (document.RequestOpen(vol, path))
                    ReportLoaded();
                else if (reload)
                    InvokeReloadConfirmDialog();
                else
                    InvokeDirtySaveLoadDialog();
            }
            catch (Exception ex) when (DelegateDialog.CanDisplayFileError(ex))
            {
                var choices = new List<string> { "Retry", "Cancel" };
                var actions = new List<DialogAction> { DelegateLoadContents, DelegateCancel };
                dialog.Invoke(this, "Cannot load the file", "Cannot load \"" + document.PendingPath + "\": " + ex.Message, choices, actions);
            }
        }

        private void ReportLoaded()
        {
            // A new file is shown from its start:
            follow.Reset();
            scroll = 0;
            caretHome = true;
            saveError = null;
            notice = null;
            if (document.IsNewFile) term.Print("[New File]");
        }

        // The confirmations name their actions (step 5, Q3); the last choice is the one Esc picks.

        protected void InvokeDirtySaveExitDialog()
        {
            var choices = new List<string> { "Save and close", "Close without saving", "Cancel" };
            var actions = new List<DialogAction> { DelegateSaveExit, DelegateNoSaveExit, DelegateCancel };
            dialog.Invoke(this, "Unsaved changes", "\"" + document.ActivePath + "\" has been edited. Save it before closing?", choices, actions);
        }

        protected void InvokeDirtySaveLoadDialog()
        {
            var choices = new List<string> { "Save, then open", "Open without saving", "Cancel" };
            var actions = new List<DialogAction> { DelegateSaveThenLoad, DelegateLoadContents, DelegateCancel };
            dialog.Invoke(this, "Unsaved changes", "\"" + document.ActivePath + "\" has been edited. Save it before opening \"" + document.PendingPath + "\"?", choices, actions);
        }

        protected void InvokeReloadConfirmDialog()
        {
            var choices = new List<string> { "Discard and reload", "Cancel" };
            var actions = new List<DialogAction> { DelegateLoadContents, DelegateCancel };
            dialog.Invoke(this, "Unsaved changes", "\"" + document.ActivePath + "\" has been edited. Throw away the changes and reload it?", choices, actions);
        }

        protected static void DelegateSaveExit(KOSTextEditPopup me)
        {
            me.SaveContents();
            me.Freeze(false);
            me.Close();
        }

        protected static void DelegateNoSaveExit(KOSTextEditPopup me)
        {
            me.document.DiscardChanges();
            me.Freeze(false);
            me.Close();
        }

        protected static void DelegateSaveThenLoad(KOSTextEditPopup me)
        {
            me.SaveContents();
            DelegateLoadContents(me);
        }

        protected static void DelegateLoadContents(KOSTextEditPopup me)
        {
            me.document.LoadPending();
            me.ReportLoaded();
        }

        protected static void DelegateCancel(KOSTextEditPopup me)
        {
            me.document.CancelOpen();
            if (me.document.ActiveVolume == null) me.Close();
        }

        protected void CheckKeyboard()
        {
            if (Event.current.type == EventType.KeyDown)
            {
                switch (Event.current.keyCode)
                {
                    case KeyCode.PageUp:
                        DoPageUp();
                        Event.current.Use();
                        break;

                    case KeyCode.PageDown:
                        DoPageDown();
                        Event.current.Use();
                        break;

                    case KeyCode.E:
                        if (Event.current.control)
                        {
                            ExitEditor();
                            Event.current.Use();
                        }
                        break;

                    case KeyCode.S:
                        if (Event.current.control)
                        {
                            TrySave();
                            Event.current.Use();
                        }
                        break;

                    case KeyCode.R:
                        if (Event.current.control)
                        {
                            ReloadContents();
                            Event.current.Use();
                        }
                        break;
                }
            }
        }

        /// <summary>The lines in view, for the page keys.</summary>
        private int RowsInView()
        {
            float line = textStyle == null ? fontHeight : Math.Max(1f, textStyle.lineHeight);
            return Math.Max(1, (int)(viewHeight / line));
        }

        protected void DoPageUp()
        {
            var editor = GetWidgetController();
            if (editor == null) return;

            // Seems to be no way to move more than one line at
            // a time - so have to do this:
            int pos = Math.Min(editor.cursorIndex, contents.Length - 1);
            int rows = RowsInView();
            while (rows > 0 && pos >= 0)
            {
                if (contents[pos] == '\n')
                    rows--;
                pos--;
                editor.MoveLeft();  // there is a MoveUp but it doesn't work.
            }
        }

        protected void DoPageDown()
        {
            var editor = GetWidgetController();
            if (editor == null) return;

            // Seems to be no way to move more than one line at
            // a time - so have to do this:
            int pos = Math.Min(editor.cursorIndex, contents.Length - 1);
            int rows = RowsInView();
            while (rows > 0 && pos < contents.Length)
            {
                if (pos >= 0 && contents[pos] == '\n')
                    rows--;
                pos++;
                editor.MoveRight(); // there is a MoveDown but it doesn't work.
            }
        }

        private void ProcessWindow(int windowId)
        {
            FlatWindowLayout flat = Layout;
            float width = WindowRect.width, height = WindowRect.height;
            bool interactive = !InteractionFrozen;

            var buttonWidths = new List<float>
            {
                flat.ButtonWidth(KOSStyle.Measure(KOSStyle.CenteredLabel(flat.FontSize), RELOAD_BUTTON_TEXT)),
                flat.ButtonWidth(KOSStyle.Measure(KOSStyle.CenteredLabel(flat.FontSize), SAVE_BUTTON_TEXT))
            };
            List<ScreenRect> headerButtons = flat.HeaderButtons(width, buttonWidths);
            float buttonsLeft = headerButtons[0].X;
            var keep = new ScreenRect(buttonsLeft, 0, width - buttonsLeft, flat.HeaderHeight);

            if (interactive)
                CheckKeyboard();
            // Moved and resized also without the keyboard, as the terminal; not under a confirmation:
            if (!DialogShown)
                BeginResizeAtEdge(flat, width, height, keep);

            float titleRight = buttonsLeft - (isDirty ? flat.HeaderHeight / 2f : 0);
            float titleEnd = KOSStyle.FlatWindow(flat, width, height, BuildTitle(), titleRight);
            if (isDirty)
                KOSStyle.DrawIcon(new Rect(titleEnd + flat.Padding / 2f, 0, flat.HeaderHeight / 2f, flat.HeaderHeight), KOSStyle.Icon.Dot,
                    Math.Max(4, (int)Math.Round(flat.FontSize * 0.55)), KOSStyle.Accent);

            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && interactive;
            try
            {
                DrawText(flat, ToRect(flat.Body(width, height, FlatWindowBottom.Strip)), interactive);

                if (KOSStyle.Button(ToRect(headerButtons[0]), RELOAD_BUTTON_TEXT, flat.FontSize))
                    ReloadContents();
                if (KOSStyle.Button(ToRect(headerButtons[1]), SAVE_BUTTON_TEXT, flat.FontSize))
                    TrySave();
                if (KOSStyle.IconButton(ToRect(flat.CloseButton(width)), KOSStyle.Icon.Close, IconSize(flat)))
                    ExitEditor();
            }
            finally
            {
                GUI.enabled = wasEnabled;
            }

            DrawStrip(flat, width, height);
            KOSStyle.FlatWindowFrame(flat, width, height, HasKeyboard);
            DrawResizeFeedback(flat, width, height, keep);
            GUI.DragWindow(ToRect(flat.Header(width)));
        }

        private static int IconSize(FlatWindowLayout flat)
        {
            return Math.Max(6, (int)Math.Round(flat.HeaderHeight * 0.6));
        }

        /// <summary>
        /// The text on the terminal's background, scrolled in <paramref name="view"/>; the wheel
        /// and the scroll bar move the view, which follows the caret when it moves (EDIT-03).
        /// </summary>
        private void DrawText(FlatWindowLayout flat, Rect view, bool interactive)
        {
            KOSStyle.Fill(view, backgroundColor);
            viewHeight = view.height;
            float gutter = flat.ScrollBarWidth + flat.Gap;
            float textWidth = Math.Max(0, view.width - gutter);
            var measured = new GUIContent(contents);
            float contentHeight = Math.Max(view.height, textStyle.CalcHeight(measured, textWidth));
            float line = Math.Max(1f, textStyle.lineHeight);

            Event e = Event.current;
            if (e.type == EventType.ScrollWheel && view.Contains(e.mousePosition))
            {
                scroll = PixelScroll.Clamp(scroll + e.delta.y * line, contentHeight, view.height);
                e.Use();
            }

            string edited;
            GUI.BeginGroup(view);
            try
            {
                edited = KOSStyle.TextArea(new Rect(0, -scroll, textWidth, contentHeight), TEXT_CONTROL, contents, textStyle, caretColor, selectionColor);
            }
            finally
            {
                GUI.EndGroup();
            }
            // Update before action buttons, including same-length replacements.
            if (interactive && !string.Equals(edited, contents, StringComparison.Ordinal))
            {
                contents = edited;
                saveError = null;
            }

            UnityEngine.TextEditor editor = GetWidgetController();
            if (caretHome)
            {
                caretIndex = 0;
                if (editor != null)
                    editor.cursorIndex = editor.selectIndex = 0;
                caretHome = false;
            }
            else if (editor != null)
                caretIndex = Math.Min(editor.cursorIndex, contents.Length);

            if (focusTextPending && interactive)
            {
                if (editor != null)
                    focusTextPending = false;
                else
                    GUI.FocusControl(TEXT_CONTROL); // takes effect in a later pass
            }

            float caretTop = textStyle.GetCursorPixelPosition(new Rect(0, 0, textWidth, contentHeight), measured, caretIndex).y;
            scroll = follow.Follow(scroll, caretIndex, contents, caretTop, line, contentHeight, view.height);

            if (contentHeight > view.height)
                scroll = scrollBar.Draw(flat.ScrollTrack(ToScreenRect(view)), scroll, contentHeight, view.height, flat.MinimumThumb);
            else
                scrollBar.Release();
        }

        /// <summary>The caret's line and column, the modified mark or a save error, a notice; the grip.</summary>
        private void DrawStrip(FlatWindowLayout flat, float width, float height)
        {
            Rect strip = ToRect(flat.Strip(width, height));
            KOSStyle.Fill(strip, KOSStyle.ChromeBackground);
            GUIStyle label = KOSStyle.Label(flat.FontSize);
            Rect grip = ToRect(flat.Grip(width, height));
            var text = new Rect(strip.x + flat.Padding, strip.y, Math.Max(0, grip.x - strip.x - 2 * flat.Padding), strip.height);

            if (saveError != null)
            {
                KOSStyle.Text(text, Fit(saveError, text.width, label), label, KOSStyle.Warning);
            }
            else
            {
                string position = TextPosition.Of(contents, caretIndex).ToString();
                if (isDirty)
                    position += KOSStyle.Separator + "modified";
                float positionWidth = Math.Min(text.width, KOSStyle.Measure(label, position));
                KOSStyle.Text(new Rect(text.x, text.y, positionWidth, text.height), Fit(position, text.width, label), label, KOSStyle.ChromeText);
                if (notice != null && DateTime.Now < noticeEnd)
                {
                    float x = text.x + positionWidth + 2 * flat.Padding;
                    var room = new Rect(x, text.y, Math.Max(0, text.xMax - x), text.height);
                    KOSStyle.Text(room, Fit(notice, room.width, label), label, KOSStyle.Accent);
                }
            }
            KOSStyle.DrawIcon(grip, KOSStyle.Icon.Grip, IconSize(flat), KOSStyle.DimText);
        }

        private static string Fit(string text, float width, GUIStyle style)
        {
            return StatusStrip.Shorten(text ?? string.Empty, width, measured => KOSStyle.Measure(style, measured), KOSStyle.Ellipsis);
        }

        // Moving, resizing, staying on screen (EDIT-04):

        private static Rect ToRect(ScreenRect rect)
        {
            return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        private static ScreenRect ToScreenRect(Rect rect)
        {
            return new ScreenRect(rect.x, rect.y, rect.width, rect.height);
        }

        private static Vector2 MouseOnScreen()
        {
            return new Vector2(Input.mousePosition.x, UnityEngine.Screen.height - Input.mousePosition.y);
        }

        /// <summary>On screen, and no smaller than the minimum (the UI scale may have grown).</summary>
        private void KeepOnScreen()
        {
            FlatWindowLayout flat = Layout;
            Rect rect = WindowRect;
            rect.width = Math.Max(rect.width, flat.EditorMinimumWidth);
            rect.height = Math.Max(rect.height, flat.EditorMinimumHeight);
            WindowRect = ToRect(WindowPlacement.KeepOnScreen(ToScreenRect(rect), UnityEngine.Screen.width, UnityEngine.Screen.height));
        }

        /// <summary>A press on an edge, a corner or the grip starts a resize drag, followed in <see cref="FollowResizeDrag"/>.</summary>
        private void BeginResizeAtEdge(FlatWindowLayout flat, float width, float height, ScreenRect keep)
        {
            Event current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0 || resizeEdges != ResizeEdges.None) return;
            ResizeEdges edges = WindowResize.EdgesAt(flat, width, height, current.mousePosition.x, current.mousePosition.y, keep);
            if (edges == ResizeEdges.None) return;
            resizeEdges = edges;
            resizeStartRect = WindowRect;
            resizeStartMouse = MouseOnScreen();
            current.Use();
        }

        /// <summary>While a resize drag goes on, the window follows the mouse on the screen; called before the window is drawn.</summary>
        private void FollowResizeDrag()
        {
            if (resizeEdges == ResizeEdges.None) return;
            if (!Input.GetMouseButton(0))
            {
                resizeEdges = ResizeEdges.None;
                return;
            }
            FlatWindowLayout flat = Layout;
            Vector2 delta = MouseOnScreen() - resizeStartMouse;
            WindowRect = ToRect(WindowResize.Drag(ToScreenRect(resizeStartRect), resizeEdges, delta.x, delta.y,
                flat.EditorMinimumWidth, flat.EditorMinimumHeight, UnityEngine.Screen.width, UnityEngine.Screen.height));
        }

        /// <summary>Over the frame: the edges a press would resize, or are resizing, in the accent color.</summary>
        private void DrawResizeFeedback(FlatWindowLayout flat, float width, float height, ScreenRect keep)
        {
            Event current = Event.current;
            if (current.type != EventType.Repaint) return;
            ResizeEdges edges = resizeEdges;
            if (edges == ResizeEdges.None && !DialogShown && !Input.GetMouseButton(0) && !scrollBar.IsDragging &&
                IsInsideMyExposedPortion(MouseOnScreen()))
                edges = WindowResize.EdgesAt(flat, width, height, current.mousePosition.x, current.mousePosition.y, keep);
            float thickness = Math.Max(2, 2 * flat.Border);
            if ((edges & ResizeEdges.Left) != 0)
                KOSStyle.Fill(new Rect(0, 0, thickness, height), KOSStyle.Accent);
            if ((edges & ResizeEdges.Right) != 0)
                KOSStyle.Fill(new Rect(width - thickness, 0, thickness, height), KOSStyle.Accent);
            if ((edges & ResizeEdges.Top) != 0)
                KOSStyle.Fill(new Rect(0, 0, width, thickness), KOSStyle.Accent);
            if ((edges & ResizeEdges.Bottom) != 0)
                KOSStyle.Fill(new Rect(0, height - thickness, width, thickness), KOSStyle.Accent);
        }

        /// <summary>
        /// The editor text's controller, while the text has the keyboard (Unity gives no way to
        /// ask a widget's by its id, so the focused control's name is checked first). Null otherwise.
        /// Only from OnGUI.
        /// </summary>
        // Return type needs full namespace path because kOS namespace has a TextEditor class too:
        protected UnityEngine.TextEditor GetWidgetController()
        {
            if (GUI.GetNameOfFocusedControl() != TEXT_CONTROL) return null;
            return (UnityEngine.TextEditor)
                GUIUtility.GetStateObject(typeof(UnityEngine.TextEditor), GUIUtility.keyboardControl);
        }

        public Rect GetRect()
        {
            return WindowRect;
        }

        protected string BuildTitle()
        {
            return document.ActivePath == null ? "Text Editor" : document.ActivePath.ToString();
        }
    }
}
