using System;
using System.Collections.Generic;
using System.IO;
using kOS.Safe.Exceptions;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using UnityEngine;

namespace kOS.Screen
{
    public delegate void DialogAction( KOSTextEditPopup editor ); // a callback to invoke upon a button press.
    
    /// <summary>
    /// A GUI widget that will only appear when the text editor is asking
    /// for a confirmation.  Pass it a list of options to display and a list
    /// of actions (delegate methods) to perform corresponding to them.
    /// Until one of the options is picked, the editor will be frozen.
    /// <para>
    /// Phase 4, step 5: a flat window (<see cref="FlatWindowLayout"/>) centered over the editor
    /// and kept on screen; the first choice is drawn as the primary one, and Esc picks the
    /// last (Cancel) while the editor has the keyboard.
    /// </para>
    /// </summary>
    /// This was MEANT to be a nested class inside KOSTextEditPopup, but Unity can't
    /// handle a MonoBehaviour class nested inside a MonoBehaviour class.  It wants them
    /// all flat and globally accessible
    public class DelegateDialog : MonoBehaviour
    {
        private KOSTextEditPopup   parent;
        private string             title = "";
        private string             message = "";
        private List<string>       options;
        private List<DialogAction> actions;
        private bool               invoked;
        private string             error = "";
        private MouseBlocking.Area mouseArea;
        private FlatWindowLayout   layout;
        private Rect               windowRect;

        public bool IsInvoked { get { return invoked; } }

        public void Dismiss()
        {
            invoked = false;
            error = "";
        }
        
        
        internal static bool CanDisplayFileError(Exception exception)
        {
            return (exception is KOSPersistenceException || exception is IOException || exception is UnauthorizedAccessException)
                && !ContainsFatalOrGuiExit(exception);
        }

        private static bool ContainsFatalOrGuiExit(Exception exception)
        {
            // Volume implementations may wrap errors. Never turn fatal/GUI-exit branches
            // into an apparently retryable file failure, including aggregate branches.
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (current is ExitGUIException || current is OutOfMemoryException || current is StackOverflowException ||
                    current is AccessViolationException || current is System.Threading.ThreadAbortException ||
                    current is AppDomainUnloadedException)
                    return true;
                var aggregate = current as AggregateException;
                if (aggregate != null)
                    foreach (Exception inner in aggregate.InnerExceptions)
                        if (ContainsFatalOrGuiExit(inner)) return true;
            }
            return false;
        }

        public void Invoke(KOSTextEditPopup parent, string message, List<string> options, List<DialogAction> actions )
        {
            Invoke(parent, "Confirm", message, options, actions);
        }

        public void Invoke(KOSTextEditPopup parent, string title, string message, List<string> options, List<DialogAction> actions )
        {
            this.parent = parent;
            this.parent.Freeze(true);
            this.title = title;
            this.message = message;
            this.options = options;
            this.actions = actions;
            error = "";
            invoked = true;
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

        private List<float> ButtonWidths(FlatWindowLayout flat)
        {
            var widths = new List<float>();
            foreach (string option in options)
                widths.Add(flat.ButtonWidth(KOSStyle.Measure(KOSStyle.CenteredBoldLabel(flat.FontSize), option)));
            return widths;
        }

        private float BodyHeight(FlatWindowLayout flat, float bodyWidth)
        {
            float height = KOSStyle.MeasureHeight(KOSStyle.Paragraph(flat.FontSize), message, bodyWidth);
            if (error.Length > 0)
                height += flat.Padding + KOSStyle.MeasureHeight(KOSStyle.Paragraph(flat.FontSize), error, bodyWidth);
            return (float)Math.Ceiling(height);
        }

        public void OnGUI()
        {
            if (invoked && parent.IsOpen)
            {
                Event e = Event.current;
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && parent.HasKeyboard && options.Count > 0)
                {
                    e.Use();
                    Answer(options.Count - 1);
                    return;
                }

                FlatWindowLayout flat = Layout;
                float screenWidth = UnityEngine.Screen.width, screenHeight = UnityEngine.Screen.height;
                float buttons = 2 * flat.Padding + flat.Gap * (options.Count - 1);
                foreach (float width in ButtonWidths(flat))
                    buttons += width;
                float windowWidth = Math.Max(flat.DialogWidth, buttons);
                if (screenWidth > 0) windowWidth = Math.Min(windowWidth, screenWidth);
                float windowHeight = flat.WindowHeight(BodyHeight(flat, windowWidth - 2 * flat.Padding), FlatWindowBottom.Buttons);
                Rect editor = parent.GetRect();
                ScreenRect placed = WindowPlacement.CenteredOver(new ScreenRect(editor.x, editor.y, editor.width, editor.height),
                    windowWidth, windowHeight, screenWidth, screenHeight);
                windowRect = new Rect(placed.X, placed.Y, placed.Width, placed.Height);

                int id = parent.GetUniqueId() + 1;
                Rect drawn = GUI.Window(id, windowRect, DrawConfirm, GUIContent.none, GUIStyle.none);
                GUI.BringWindowToFront(id);
                KOSMouseBlocking.Show(ref mouseArea, "DelegateDialog", drawn);
            }
        }

        public void OnDestroy()
        {
            KOSMouseBlocking.Unregister(ref mouseArea);
        }
        
        public void DrawConfirm( int windowID )
        {
            if (!invoked) return;
            FlatWindowLayout flat = Layout;
            float width = windowRect.width, height = windowRect.height;
            KOSStyle.FlatWindow(flat, width, height, title, width);

            ScreenRect body = flat.Body(width, height, FlatWindowBottom.Buttons);
            GUIStyle paragraph = KOSStyle.Paragraph(flat.FontSize);
            float messageHeight = KOSStyle.MeasureHeight(paragraph, message, body.Width);
            KOSStyle.Text(new Rect(body.X, body.Y, body.Width, messageHeight), message, paragraph, KOSStyle.ChromeText);
            if (error.Length > 0)
            {
                float y = body.Y + messageHeight + flat.Padding;
                KOSStyle.Text(new Rect(body.X, y, body.Width, Math.Max(0, body.Y + body.Height - y)), error, paragraph, KOSStyle.Warning);
            }

            List<ScreenRect> buttons = flat.Buttons(width, height, ButtonWidths(flat));
            for (int bNum = 0 ; bNum < options.Count ; bNum++)
            {
                ScreenRect where = buttons[bNum];
                if (KOSStyle.Button(new Rect(where.X, where.Y, where.Width, where.Height), options[bNum], flat.FontSize, bNum == 0))
                {
                    Answer(bNum);
                    break;
                }
            }
            KOSStyle.FlatWindowFrame(flat, width, height, true);
        }

        private void Answer(int bNum)
        {
            try
            {
                actions[bNum](parent);
                Dismiss();
                parent.Freeze(false);
                if (parent.IsOpen)
                {
                    GUI.FocusWindow(parent.GetUniqueId());
                    // The click on this dialog may have counted as outside the editor;
                    // give the keyboard back to it, as unfreezing it always did (INPUT-01).
                    parent.GetFocus();
                }
            }
            catch (Exception ex) when (CanDisplayFileError(ex))
            {
                // Leave the transaction and choices intact for retry/discard/cancel.
                // Fatal errors and Unity GUI-exit control flow are not intercepted.
                error = ex.Message;
            }
        }
    }
}