using System;
using System.Linq;
using System.Collections.Generic;
using kOS.Safe.Persistence;
using UnityEngine;
using UnityEngine.Networking;
using kOS.Safe.Screen;
using kOS.Safe.Encapsulation;
using kOS.Module;
using kOS.UserIO;
using kOS.Safe.UserIO;
using KSP.UI.Dialogs;
using kOS.Safe.Utilities;
using kOS.Safe.Module;

namespace kOS.Screen
{
    // Blockotronix 550 Computor Monitor
    public class TermWindow : KOSManagedWindow , ITermWindow, ITerminalInputHost<TelnetSingletonServer>
    {
        public const string CONTROL_LOCKOUT = "kOSTerminal";

        /// <summary>
        /// Set to true only when compiling a version specifically for the purpose
        /// of debugging the use of international Unicode chars on a US-market
        /// keyboard that cannot type characters above ascii 127.  (The "alt-NNNN" method
        /// doesn't work in the terminal so you need to actually physically have the keys
        /// on the keyboard to type the letters.  This turns on a mapping of a few of
        /// the lesser used ASCII values to some other keys > 127.  This mapping
        /// will be very wrong to publish in a release, or even to use in a normal
        /// DEBUG compile (which is why it's not triggering on the DEBUG flag).
        /// ONLY set to true for testing this one feature, never for anything else.)
        /// 
        /// NOTE:  The mapping is only implemented for the in-game terminal, NOT for the
        /// telnet terminal.  This is because (presumably) the telnet terminal has other
        /// ways to type these characters (i.e. Putty can obey the ALT-numberpad technique)
        /// and doesn't need this hack-y test.
        /// </summary>
        private const bool DebugInternational = false;

        private static string root;
        private static readonly Color color = new Color(1f, 1f, 1f, 1.1f); // opaque window color when focused
        // The cells' colors (Phase 4, step 3: TerminalPalette, where their contrast is tested):
        private static readonly Color bgColor = KOSStyle.ToColor(TerminalPalette.Background);
        private static readonly Color textColor = KOSStyle.ToColor(TerminalPalette.Text);
        private static readonly Color textColorOff = KOSStyle.ToColor(TerminalPalette.UnpoweredText); // power starved
        private static readonly Color cursorColor = KOSStyle.ToColor(TerminalPalette.Cursor);
        private Vector2 resizeOldSize;
        private bool resizeMouseDown;
        private int formerCharPixelHeight;
        private int formerCharPixelWidth;
        private int postponedCharPixelHeight = -1; // a size picked with the header's buttons, applied at the next OnGUI
        
        private bool consumeEvent;
        private bool fontGotResized;
        
        private bool collapseFastBeepsToOneBeep = false; // This is a setting we might want to fiddle with depending on opinion.

        private float cursorBlinkTime;

        private Font font;
        private int fontSize;
        private string pickedFontName; // TerminalFontName when the font was last looked up
        private GUISkin terminalLetterSkin;
            
        private bool hadKeyboard;
        /// <summary>How long blinks should last for, for various blinking needs</summary>
        private readonly TimeSpan blinkDuration = System.TimeSpan.FromMilliseconds(150);
        /// <summary>How long to pad between consecutive blinks to ensure they are visibly detectable as distinct blinks.</summary>
        private readonly TimeSpan blinkCoolDownDuration = System.TimeSpan.FromMilliseconds(50);
        /// <summary>At what milliseconds-from-epoch timestamp will the current blink be over.</summary>
        private DateTime blinkEndTime;
        /// <summary>text color that changes depending on if the computer is on</summary>
        private Color currentTextColor;

        // The terminal's files that are missing, a sign of a broken install it says so for
        // (review L-04; see DrawTerminalGui): looked for once per game, by the first window.
        private static IList<string> missingInstallFiles;
        private int guiTerminalBeepsPending;

        // The chrome (Phase 4, step 1): our own header, frame and status strip, laid out by
        // TerminalChromeLayout for the UI scale and the cell size, drawn in KOSStyle.
        private TerminalChromeLayout layout;
        private float fittedScale = float.NaN; // the UI scale the window size was last worked out for
        private string headerTitle = string.Empty;

        // What the window draws on every IMGUI event, kept while it shows the same (review P-07,
        // P-08): the strip's layout, the header title as fitted, the rows' strings. The measurers
        // are made once; each measures with the style set just before it is used.
        private readonly StatusStripCache stripCache = new StatusStripCache();
        private readonly ShortenedText shownTitle = new ShortenedText();
        private readonly TerminalRowTexts rowTexts = new TerminalRowTexts();
        private GUIStyle stripLabel;
        private GUIStyle titleLabel;
        private Func<string, float> measureStripText;
        private Func<string, float> measureTitleText;

        // Geometry and scrolling (Phase 4, step 2). The window's position, cells and character
        // height are saved per CPU (TerminalGeometryFile) when they change, at most every
        // geometrySaveInterval and not during a drag, and once more when the window goes:
        private ITerminalGeometryStore geometryStore;
        private TerminalGeometry savedGeometry; // what the store holds, as far as this window knows
        private DateTime nextGeometryCheck;
        private static readonly TimeSpan geometryCheckInterval = TimeSpan.FromSeconds(0.5);
        private static readonly TimeSpan geometrySaveInterval = TimeSpan.FromSeconds(2);

        // A resize drag by an edge, a corner or the grip, while resizeEdges is not None. The
        // window follows it snapped to cells; the screen takes the new size on release.
        private ResizeEdges resizeEdges;
        private readonly WindowKeeper keeper = new WindowKeeper();
        private Rect resizeStartRect;
        private Vector2 resizeStartMouse; // absolute
        private int resizeStartColumns;
        private int resizeStartRows;
        private ResizeResult resizeTarget;

        // The scroll bar's thumb being dragged, and where on it the press was:
        private bool scrollDragging;
        private float scrollGrabOffset;

        private SharedObjects shared;
        private KOSTextEditPopup popupEditor;

        // The attached telnet clients (more than one can share this terminal), the screen
        // snapshot and what the clients are sent:
        private readonly TerminalSession<TelnetSingletonServer> session;
        
        private readonly TerminalInputRouter<TelnetSingletonServer> inputRouter;

        // Ctrl+V and Shift+Insert (P3-3); a paste that runs lines waits for the confirmation drawn here:
        private readonly TerminalGuiPaste<TelnetSingletonServer> guiPaste;
        private MouseBlocking.Area pasteConfirmationMouseArea;
        private GUIStyle pastePreviewStyle;
        private FlatWindowLayout flatLayout;
        private Rect pasteRect;

        // Ctrl+Shift+C and Ctrl+Shift+E (P3-4) say what they did in the status strip, for a moment:
        private string copyNotice;
        private DateTime copyNoticeEnd;
        private static readonly TimeSpan copyNoticeDuration = TimeSpan.FromSeconds(2);
        
        // The CPU state (Phase 3, D2), in the status strip and after the telnet title; worked
        // out at most every statusInterval, which also bounds the title updates telnet clients get:
        private string baseTitle;
        private TerminalStatus status; // null until worked out, or when that failed

        /// <summary>The CPU's state as last worked out (every 0.25 s); null before that or when it failed. Read by the toolbar panel.</summary>
        public TerminalStatus Status { get { return status; } }

        /// <summary>
        /// Set by the toolbar panel while its CPUs tab shows (Phase 4 step 4): every terminal then
        /// works out its state, also with its window closed and no telnet client.
        /// </summary>
        internal static bool StatusWanted { get; set; }
        private string statusText = string.Empty; // its text, after the telnet title
        private DateTime nextStatusTime;
        private static readonly TimeSpan statusInterval = TimeSpan.FromSeconds(0.25);
        private bool reportedStatusFailure;
        private readonly WaitingDisplay waitingDisplay = new WaitingDisplay(); // STATUS-01

        private readonly WheelScroll wheelScroll = new WheelScroll(); // O1

        // Mouse selection of cells (O3): dragging over the characters selects instead of
        // moving the window; dragging past the top or bottom scrolls the view.
        private readonly TerminalSelection selection = new TerminalSelection();
        private Vector2 lastDragPosition;
        private DateTime nextSelectionScroll;
        private static readonly TimeSpan selectionScrollInterval = TimeSpan.FromMilliseconds(50);

        /// <summary>The terminal's name (the GUI window's title; telnet clients also get the state after it).</summary>
        public string TitleText { get { return baseTitle; } }

        private readonly RenderFaultRecovery renderFaultRecovery = new RenderFaultRecovery();
        private bool resizeFromFault;
        
        private bool uiGloballyHidden = false;

        private Sound.SoundMaker soundMaker;        

        public TermWindow()
        {
            IsPowered = true;
            WindowRect = new Rect(50, 60, 0, 0); // will get resized later in AttachTo().
            session = new TerminalSession<TelnetSingletonServer>(DateTime.Now);
            inputRouter = new TerminalInputRouter<TelnetSingletonServer>(this);
            guiPaste = new TerminalGuiPaste<TelnetSingletonServer>(inputRouter, () => shared == null ? null : shared.Screen);
        }

        public bool ShowCursor { get; set; }

        public void Awake()
        {
            if (missingInstallFiles == null)
            {
                missingInstallFiles = TerminalInstallCheck.Missing(KSPUtil.ApplicationRootPath);
                foreach (string file in missingInstallFiles)
                    UnityEngine.Debug.LogWarning("kOS: missing " + file + " under the game's folder: kOS is not installed where it should be");
            }

            popupEditor = gameObject.AddComponent<KOSTextEditPopup>();
            popupEditor.SetUniqueId(UniqueId + 5);
            
            terminalLetterSkin = BuildPanelSkin();

            GameEvents.onHideUI.Add (OnHideUI);
            GameEvents.onShowUI.Add (OnShowUI);

            soundMaker = gameObject.AddComponent<Sound.SoundMaker>();
        }


        /// <summary>
        /// The window goes with its processor (P2-3): the telnet clients are detached with a
        /// message (the session is disposed), the screen forgets this window's resize notifier,
        /// and the skin cloned in <see cref="Awake"/> is destroyed. The textures are shared
        /// (<see cref="KOSStyle"/>) and stay.
        /// </summary>
        new public void OnDestroy()
        {
            SaveGeometryIfChanged(true); // scene change, vessel unloaded: the last move is kept
            LoseFocus();
            guiPaste.Cancel();
            KOSMouseBlocking.Unregister(ref pasteConfirmationMouseArea);
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);

            session.Dispose();
            if (shared != null && shared.Screen != null)
                shared.Screen.RemoveResizeNotifier(NotifyOfScreenResize);
            Utilities.Utils.DestroyCreated(ref terminalLetterSkin);

            base.OnDestroy();
        }
        
        public kOS.Safe.Sound.ISoundMaker GetSoundMaker()
        {
            return soundMaker;
        }

        public void OpenPopupEditor(Volume v, GlobalPath path)
        {
            popupEditor.AttachTo(this, v, path);
            popupEditor.Open();
        }
        
        void OnHideUI()
        {
            uiGloballyHidden = true;
        }
        
        void OnShowUI()
        {
            uiGloballyHidden = false;            
        }
        
        public override void Open(bool takeFocus)
        {
            base.Open(takeFocus);
            BringToFront();
            keeper.FitWhollyNext(); // a window left partly off screen comes back on (DRAG-01)
            guiTerminalBeepsPending = 0; // Closing and opening the window will wipe pending beeps from the beep queue.
            nextStatusTime = DateTime.MinValue; // the strip's state, not the one from before it was closed
        }
            
        /// <param name="recalcWidth">If true, recalc the font width from height if there's any font change.
        /// Setting this to true should only be done from inside an OnGUI call.
        /// If you call this from outside an OnGUI context, set this to false.</param>
        private void GetFontIfChanged(bool recalcWidth)
        {
            int newSize = shared.Screen.CharacterPixelHeight;
            string newName =  SafeHouse.Config.TerminalFontName;
            if (fontSize != newSize || !string.Equals(pickedFontName, newName))
            {
                fontSize = newSize;
                pickedFontName = newName;
                // The user's pick, then the fallbacks (Phase 4 step 3c: Lucida Console before the Courier ones):
                font = AssetManager.Instance.GetSystemFontByNameAndSize(TerminalFontChoice.NamesToTry(newName), fontSize, false);

                terminalLetterSkin.label.font = font;
                terminalLetterSkin.label.fontSize = fontSize;
                if (recalcWidth)
                {
                    CharacterInfo chInfo;
                    terminalLetterSkin.label.font.RequestCharactersInTexture("X"); // Make sure the char in the font is lazy-loaded by Unity.
                    terminalLetterSkin.label.font.GetCharacterInfo('X', out chInfo);
                    shared.Screen.CharacterPixelWidth = chInfo.advance;

                    NotifyOfScreenResize(shared.Screen);
                }
            }
        }

        public override void Close()
        {
            // Diable GUI and release all locks
            guiPaste.Cancel(); // a paste waiting for confirmation is dropped with the window
            resizeEdges = ResizeEdges.None; // a drag cut short changes nothing
            scrollDragging = false;
            rowTexts.Clear(); // the rows' lines and strings can go while nothing draws them
            base.Close();
        }

        public void Toggle()
        {
            Toggle(true);
        }

        /// <param name="takeFocus">False for an action group or a script.</param>
        public void Toggle(bool takeFocus)
        {
            if (IsOpen) Close();
            else Open(takeFocus);
        }

        /// <summary>
        /// Bring the shared input policy up to date and notice a gained keyboard focus. The
        /// control lock itself (<see cref="CONTROL_LOCKOUT"/>, all controls, which also covers
        /// the VAB/SPH editor) is set and removed by <see cref="KOSInputOwnership"/>.
        /// </summary>
        private bool RefreshKeyboard()
        {
            KOSInputOwnership.Update();
            bool hasKeyboard = HasKeyboard;
            if (hasKeyboard && !hadKeyboard)
                ShowCursor = true; // as focusing always did: a CPU loaded already powered never set it
            hadKeyboard = hasKeyboard;
            return hasKeyboard;
        }

        void OnGUI()
        {
            if (!IsOpen || !IsPowered)
                guiPaste.Cancel(); // nobody can answer the confirmation (IsOpen may be set without Close())
            if (!IsOpen) return;

            if (postponedCharPixelHeight >= 0 && shared != null && shared.Screen != null)
            {
                // Picked with the header's buttons while the window drew; changed between
                // passes, as the window's size follows it.
                shared.Screen.CharacterPixelHeight = postponedCharPixelHeight;
                postponedCharPixelHeight = -1;
            }

            // Keep font/geometry preparation before GUI.Window, but contain its failures
            // so the callback can still offer fallback Close/Retry/Resize controls.
            renderFaultRecovery.TryRender(RenderTimeSeconds(), PrepareTerminalRendering, CanRecoverRenderFault, ReportRenderFault);

            // Hidden UI and the flight-results dialog suspend the keyboard focus, so a terminal
            // that is not drawn does not read keys (INPUT-01):
            if (RefreshKeyboard()) ProcessKeyEvents();
            if (FlightResultsDialog.isDisplaying) return;
            if (uiGloballyHidden)
            {
                kOS.Safe.Encapsulation.IConfig cfg = kOS.Safe.Utilities.SafeHouse.Config;
                if (cfg == null || cfg.ObeyHideUI)
                    return;
            }

            GUI.skin = HighLogic.Skin;
            
            GUI.color = color;

            FollowResizeDrag();
            // Restored, opened, the resolution, UI scale or size changed: the window comes wholly on screen (step 2, DRAG-01).
            KeepOnScreen();
            // No KSP window skin or title: the window draws its own header and frame (Phase 4).
            WindowRect = GUI.Window(UniqueId, WindowRect, TerminalGui, GUIContent.none, GUIStyle.none);
            KeepOnScreen(); // moved by its header or strip: partly off screen is allowed (DRAG-01)
            BlockMouseOver(WindowRect);
            if (guiPaste.IsAwaitingConfirmation)
                DrawPasteConfirmation();
            
            if (consumeEvent)
            {
                consumeEvent = false;
                Event.current.Use();
            }
        }
        
        void Update()
        {
            if (shared == null || shared.Vessel == null || shared.Vessel.parts.Count == 0)
            {
                // Holding onto a vessel instance that no longer exists?
                Close();
                return; // nothing below works without it (R-04); OnDestroy still detaches the telnet clients
            }
            RefreshStatus();
            if (selection.IsDragging && !Input.GetMouseButton(0))
                selection.EndDrag(); // released outside the window
            if (scrollDragging && !Input.GetMouseButton(0))
                scrollDragging = false;
            SaveGeometryIfChanged(false);
            // The snapshot for the GUI, and the telnet clients' output, at most every 1/20th second
            // (the snapshot only while the window is open or a client is attached: P-03):
            bool telnetsGotRepainted = session.Update(DateTime.Now, IsOpen);
            ProcessTelnetInput(); // want to do this even when the terminal isn't actually displaying.
            ProcessUnconsumedInput(); // Moved here from OnGUI because it needs to run even when the GUI terminal is closed.
            if (telnetsGotRepainted)
            {
                // Move the beeps from the screenbuffer "queue" to my own local terminal "queue".
                // This is only done in the Update() when telentRepaint got triggered
                // because otherwise the GUI terminal could wipe shared.Screen.BeepsPending to zero during an update
                // where the telnet terminal never saw the beeps pending and never knew to print the beeps out.
                guiTerminalBeepsPending += shared.Screen.BeepsPending;
                shared.Screen.BeepsPending = 0; // Presume all the beeping has been dealt with for both the telnets and the GUI terminal.
            }

            if (!IsOpen ) return;

            UpdateLogic();
            UpdateGUIBeeps();

            if (!RefreshKeyboard()) return;

            cursorBlinkTime += Time.deltaTime;
            if (cursorBlinkTime > 1) cursorBlinkTime -= 1;
        }
        
        void UpdateGUIBeeps()
        {
            // Eat just one beep off the pending queue.  Wait for a future
            // Update() to consume more, if there are any more:
            if (guiTerminalBeepsPending > 0)
            {
                // Behavior depending on whether we'd like BEEP, BEEP, BEEP to
                // emit 3 back to back beeps, or just one beep ingoring overlapping beeps.
                // Many hardware terminals only emit one beep when given a string
                // of lots of beeps.
                if (collapseFastBeepsToOneBeep)
                {
                    Beep();
                    guiTerminalBeepsPending = 0;
                }
                else
                {
                    if (Beep())
                        --guiTerminalBeepsPending;
                }
            }
        }
        
        /// <summary>
        /// Attempts to make Unity start a playthrough of the beep audio clip.  The playthrough will continue
        /// on its own in the background while the rest of the code continues on.  It will do nothing if the previous
        /// beep is still being played.  (We only gave ourselves one audio beep source per GUI terminal so we can't play beeps
        /// simultaneously.)<br/>
        /// Addendum: It will redirect into a visual beep if that is called for instead.
        /// </summary>
        /// <returns>true if it beeped.  false if it coudn't beep yet (because the audio source is still busy emitting the previous beep).</returns>
        bool Beep()
        {
            if (shared.Screen.VisualBeep)
            {
                DateTime nowTime = DateTime.Now;
                if (nowTime < (blinkEndTime + (blinkCoolDownDuration))) // prev blink not done yet.
                    return false;
                
                // Turning this timer on tells GUI repainter elsewhere in this class to paint in reverse until it expires:
                blinkEndTime = nowTime + blinkDuration;
            }
            else
            {
                return soundMaker.BeginFileSound("beep");
            }
            return true;
        }

        /// <summary>
        /// Process the GUI event handler key events that are being seen the by GUI terminal,
        /// by translating them into values from the UnicodeCommand enum first if need be,
        /// and then passing them through to ProcessOneInputChar().
        /// </summary>
        void ProcessKeyEvents()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                // This ugly hack is here to solve a bug with Unity mapping
                // Keycodes to Unicode chars incorrectly on its Linux version:
                char c;
                if ((e.character & 0xff00) == 0xff00) // Only trigger on Unicode values 0xff00 through 0xffff, to avoid issue #2061
                    c = (char)(e.character & 0x007f); // When doing this to solve issue #206 (yes, #206, separate from #2061 above)
                else
                    c = e.character;

                if (guiPaste.IsAwaitingConfirmation)
                {
                    ProcessKeyWhileConfirmingPaste(e);
                    return;
                }

                // Copy (P3-4): Ctrl+Shift+C the rows in view, Ctrl+Shift+E the last error. Before
                // P3-4 they were Ctrl+C (BREAK) and Ctrl+E (end of line), with shift ignored.
                if (e.keyCode == KeyCode.C && e.control && e.shift)
                {
                    CopyVisibleRows();
                    consumeEvent = true;
                    return;
                }
                if (e.keyCode == KeyCode.E && e.control && e.shift)
                {
                    CopyLastError();
                    consumeEvent = true;
                    return;
                }

                // command sequences
                if (e.keyCode == KeyCode.C && e.control) // Ctrl+C
                {
                    ProcessOneInputChar((char)UnicodeCommand.BREAK, null, true, true);
                    consumeEvent = true;
                    return;
                }

                // Paste (P3-3). Not with Alt, which AltGr adds on Windows layouts that type with AltGr+V:
                if ((e.keyCode == KeyCode.V && e.control && !e.alt) ||
                    (e.keyCode == KeyCode.Insert && e.shift && !e.control && !e.alt))
                {
                    guiPaste.Paste(ReadClipboard);
                    consumeEvent = true;
                    cursorBlinkTime = 0.0f;
                    return;
                }
                // Command used to be Control-shift-X, now we don't care if shift is down aymore, to match the telnet expereince
                // where there is no such thing as "uppercasing" a control char.
                if ((e.keyCode == KeyCode.X && e.control) ||
                    (e.keyCode == KeyCode.D && e.control) // control-D to match the telnet experience
                   )
                {
                    ProcessOneInputChar((char)0x000d, null, true, true);
                    consumeEvent = true;
                    return;
                }
                
                if (e.keyCode == KeyCode.A && e.control)
                {
                    ProcessOneInputChar((char)0x0001, null, true, true);
                    consumeEvent = true;
                    return;
                }
                
                if (e.keyCode == KeyCode.E && e.control)
                {
                    ProcessOneInputChar((char)0x0005, null, true, true);
                    consumeEvent = true;
                    return;
                }

                // History search (Phase 3): Ctrl+R starts it or finds the next older match;
                // Escape leaves it, and is otherwise left to the game as before.
                if (e.keyCode == KeyCode.R && e.control && !e.alt)
                {
                    ProcessOneInputChar(CommandLineEditor.SEARCH_KEY, null, true, true);
                    consumeEvent = true;
                    cursorBlinkTime = 0.0f;
                    return;
                }
                if (e.keyCode == KeyCode.Escape && IsSearchingHistory())
                {
                    ProcessOneInputChar(CommandLineEditor.CANCEL_KEY, null, true, true);
                    consumeEvent = true;
                    return;
                }

                if (!IsSpecial(c)) // printable characters
                {
#pragma warning disable CS0162
                    if (DebugInternational)
                        c = DebugInternationalMapping(c);
#pragma warning restore CS0162
                    ProcessOneInputChar(c, null, true, true);
                    consumeEvent = true;
                    cursorBlinkTime = 0.0f; // Don't blink while the user is still actively typing.
                }
                else if (e.keyCode != KeyCode.None)
                {
                    consumeEvent = true;
                    switch (e.keyCode)
                    {
                        case KeyCode.Tab:          ProcessOneInputChar('\t', null, true, true);                                  break;
                        case KeyCode.LeftArrow:    ProcessOneInputChar((char)UnicodeCommand.LEFTCURSORONE, null, true, true);    break;
                        case KeyCode.RightArrow:   ProcessOneInputChar((char)UnicodeCommand.RIGHTCURSORONE, null, true, true);   break;
                        case KeyCode.UpArrow:      ProcessOneInputChar((char)UnicodeCommand.UPCURSORONE, null, true, true);      break;
                        case KeyCode.DownArrow:    ProcessOneInputChar((char)UnicodeCommand.DOWNCURSORONE, null, true, true);    break;
                        case KeyCode.Home:         ProcessOneInputChar((char)UnicodeCommand.HOMECURSOR, null, true, true);       break;
                        case KeyCode.End:          ProcessOneInputChar((char)UnicodeCommand.ENDCURSOR, null, true, true);        break;
                        case KeyCode.PageUp:       ProcessOneInputChar((char)UnicodeCommand.PAGEUPCURSOR, null, true, true);     break;
                        case KeyCode.PageDown:     ProcessOneInputChar((char)UnicodeCommand.PAGEDOWNCURSOR, null, true, true);   break;
                        case KeyCode.Delete:       ProcessOneInputChar((char)UnicodeCommand.DELETERIGHT, null, true, true);      break;
                        case KeyCode.Backspace:    ProcessOneInputChar((char)UnicodeCommand.DELETELEFT, null, true, true);       break;
                        case KeyCode.KeypadEnter:  // (deliberate fall through to next case)
                        case KeyCode.Return:       ProcessOneInputChar((char)UnicodeCommand.STARTNEXTLINE, null, true, true);    break;
                        
                        // More can be added to the list here to support things like F1, F2, etc.  But at the moment we don't use them yet.
                        
                        // default: ignore and allow the event to pass through to whatever else wants to read it:
                        default:                   consumeEvent = false;                                             break;
                    }
                    cursorBlinkTime = 0.0f;// Don't blink while the user is still actively typing.
                }
            }
        }

        /// <summary>
        /// While a paste waits for confirmation, the keyboard answers it and types nothing:
        /// Enter runs the paste, Escape cancels it, and Ctrl+C cancels it and still sends BREAK.
        /// </summary>
        private void ProcessKeyWhileConfirmingPaste(Event e)
        {
            consumeEvent = true;
            if (e.keyCode == KeyCode.C && e.control && !e.shift)
            {
                guiPaste.Cancel();
                ProcessOneInputChar((char)UnicodeCommand.BREAK, null, true, true);
            }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                guiPaste.Confirm();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                guiPaste.Cancel();
            }
        }

        private bool IsSearchingHistory()
        {
            var editor = shared == null ? null : shared.Interpreter as CommandLineEditor;
            return editor != null && editor.IsSearchingHistory;
        }

        private static string ReadClipboard()
        {
            return GUIUtility.systemCopyBuffer;
        }

        private void CopyVisibleRows()
        {
            if (shared == null || shared.Screen == null) return;
            var screenBuffer = shared.Screen as ScreenBuffer;
            bool selected = screenBuffer != null && !selection.IsEmpty;
            // The selection when there is one (O3), else the rows in view:
            string text = selected ? TerminalCopy.SelectionText(screenBuffer, selection) : TerminalCopy.VisibleText(shared.Screen);
            if (text.Length == 0)
            {
                ShowCopyNotice("Nothing to copy");
                return;
            }
            int lines = TerminalCopy.LineCount(text);
            if (WriteClipboard(text))
                ShowCopyNotice(string.Format("Copied {0} line{1}", lines, lines == 1 ? "" : "s"));
        }

        private void CopyLastError()
        {
            var logger = shared == null ? null : shared.Logger as kOS.Logger;
            string text = logger == null ? null : logger.LastError.Text;
            if (string.IsNullOrEmpty(text))
            {
                ShowCopyNotice("No error to copy");
                return;
            }
            if (WriteClipboard(text))
                ShowCopyNotice("Copied the last error");
        }

        private bool WriteClipboard(string text)
        {
            try
            {
                GUIUtility.systemCopyBuffer = text;
                return true;
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException || ex is ExitGUIException))
            {
                UnityEngine.Debug.LogException(ex, this);
                ShowCopyNotice("Copy failed (see KSP.log)");
                return false;
            }
        }

        private void ShowCopyNotice(string notice)
        {
            copyNotice = notice;
            copyNoticeEnd = DateTime.Now + copyNoticeDuration;
        }

        private FlatWindowLayout FlatLayout
        {
            get
            {
                float scale = TerminalChromeLayout.ClampScale(KOSStyle.UiScale);
                if (flatLayout == null || flatLayout.Scale != scale)
                    flatLayout = new FlatWindowLayout(scale);
                return flatLayout;
            }
        }

        /// <summary>
        /// The paste confirmation (P3-3), over the terminal: what would run, the first lines and
        /// the two choices. Phase 4 step 5 (PASTE-UI-01): an opaque flat window centered over the
        /// terminal, the preview in the terminal's font and colors.
        /// </summary>
        private void DrawPasteConfirmation()
        {
            FlatWindowLayout flat = FlatLayout;
            if (pastePreviewStyle == null || pastePreviewStyle.font != font || pastePreviewStyle.fontSize != fontSize)
            {
                // The pasted text is shown as it is, never as Unity rich text:
                pastePreviewStyle = new GUIStyle { font = font, fontSize = fontSize, richText = false, wordWrap = false,
                    clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0) };
                pastePreviewStyle.normal.textColor = textColor;
            }
            int dialogId = UniqueId + 10;
            float width = Mathf.Round(Math.Max(flat.DialogWidth * 0.6f, Math.Min(flat.DialogWidth * 1.3f, WindowRect.width - 4 * flat.Padding)));
            width = Math.Min(width, UnityEngine.Screen.width);
            float body = PasteBodyHeight(flat, width - 2 * flat.Padding);
            float height = flat.WindowHeight(body, FlatWindowBottom.Buttons);
            ScreenRect placed = WindowPlacement.CenteredOver(ToScreenRect(WindowRect), width, height, UnityEngine.Screen.width, UnityEngine.Screen.height);
            pasteRect = ToRect(placed);
            Rect drawn = GUI.Window(dialogId, pasteRect, DrawPasteConfirmationContents, GUIContent.none, GUIStyle.none);
            GUI.BringWindowToFront(dialogId);
            KOSMouseBlocking.Show(ref pasteConfirmationMouseArea, "TermWindowPaste" + UniqueId, drawn);
        }

        private const string PASTE_HINT = "Enter runs it, Esc cancels.";

        private float PasteBodyHeight(FlatWindowLayout flat, float bodyWidth)
        {
            float question = KOSStyle.MeasureHeight(KOSStyle.Paragraph(flat.FontSize), guiPaste.Question, bodyWidth);
            float preview = KOSStyle.MeasureHeight(pastePreviewStyle, guiPaste.Preview, bodyWidth) + 2 * flat.Padding;
            float hint = KOSStyle.MeasureHeight(KOSStyle.Paragraph(flat.SmallFontSize), PASTE_HINT, bodyWidth);
            return Mathf.Ceil(question + preview + hint + 2 * flat.Padding);
        }

        private void DrawPasteConfirmationContents(int windowId)
        {
            FlatWindowLayout flat = FlatLayout;
            float width = pasteRect.width, height = pasteRect.height;
            KOSStyle.FlatWindow(flat, width, height, "Paste", width);
            Rect body = ToRect(flat.Body(width, height, FlatWindowBottom.Buttons));

            GUIStyle paragraph = KOSStyle.Paragraph(flat.FontSize);
            float y = body.y;
            float questionHeight = KOSStyle.MeasureHeight(paragraph, guiPaste.Question, body.width);
            KOSStyle.Text(new Rect(body.x, y, body.width, questionHeight), guiPaste.Question, paragraph, KOSStyle.ChromeText);
            y += questionHeight + flat.Padding;

            float previewHeight = KOSStyle.MeasureHeight(pastePreviewStyle, guiPaste.Preview, body.width) + 2 * flat.Padding;
            var preview = new Rect(body.x, y, body.width, previewHeight);
            KOSStyle.Fill(preview, bgColor);
            GUI.Label(new Rect(preview.x + flat.Padding, preview.y + flat.Padding, Math.Max(0, preview.width - 2 * flat.Padding),
                Math.Max(0, preview.height - 2 * flat.Padding)), guiPaste.Preview, pastePreviewStyle);
            y += previewHeight + flat.Padding;

            GUIStyle hint = KOSStyle.Paragraph(flat.SmallFontSize);
            KOSStyle.Text(new Rect(body.x, y, body.width, Math.Max(0, body.yMax - y)), PASTE_HINT, hint, KOSStyle.DimText);

            string confirm = guiPaste.ConfirmLabel;
            const string cancel = "Cancel";
            GUIStyle measure = KOSStyle.CenteredBoldLabel(flat.FontSize);
            List<ScreenRect> buttons = flat.Buttons(width, height, new List<float>
            {
                flat.ButtonWidth(KOSStyle.Measure(measure, confirm)),
                flat.ButtonWidth(KOSStyle.Measure(measure, cancel))
            });
            // Both asked on every event, so the controls keep their ids:
            bool run = KOSStyle.Button(ToRect(buttons[0]), confirm, flat.FontSize, true);
            bool cancelled = KOSStyle.Button(ToRect(buttons[1]), cancel, flat.FontSize);
            if (run)
                guiPaste.Confirm();
            else if (cancelled)
                guiPaste.Cancel();
            bool answered = run || cancelled;
            KOSStyle.FlatWindowFrame(flat, width, height, true);
            if (answered && IsOpen && IsPowered)
                GetFocus(); // a click on the confirmation may have counted as outside the terminal (INPUT-01)
        }

        private static bool IsSpecial(char c)
        {
            if (c < 0x0020 || c > 0xE000)
                return true;
            if (Enum.IsDefined(typeof(UnicodeCommand), (int)c))
                return true;
            return false;
        }

        private static char DebugInternationalMapping(char c)
        {
            // Hex codes are used for the unicode letters here, just
            // in case some kOS contributor tries to edit this source
            // file in a non-Unicode-aware editor that would break
            // the code.  (This way it only would break what's written
            // in the comment, not the code).
            if (c == '%')
                return (char)0x00c6; // 'Æ'
            if (c == '$')
                return (char)0x015d; // 'ŝ'
            if (c == '&')
                return (char)0x042f; // 'Я'
            if (c == '~')
                return (char)0x00f1; // 'ñ'
            return c;
        }

        /// <summary>
        /// A means to get the current terminal font size without
        /// having to expose the terminal's inner members.
        /// </summary>
        /// <returns>The font size.</returns>
        public int GetFontSize()
        {
            return shared.Screen.CharacterPixelHeight;
        }
        
        /// <summary>
        /// Read pending input from the telnet clients attached and process it, within a
        /// per-client budget each update (<see cref="TerminalInputPump"/>). Clients take
        /// turns at going first. Typed text is only moved into the type-ahead queue while
        /// that queue is short; the rest waits, in order, in the client's bounded telnet
        /// queue. Interrupts, resizes and repaint requests are always processed at once.
        /// </summary>
        private void ProcessTelnetInput()
        {
            if (session.ViewerCount == 0)
                return; // nearly always: nothing to copy or route (P-02)
            // It's possible to close and remove telnets from the list during the processing
            // of input (if the detach signal Ctrl-D is sent).  Therefore we have to
            // make this temp copy to prorect against the C# error "Collection was Modified"
            // during the foreach loop:
            inputRouter.ProcessClientInput(session.Viewers.Where(telnet => telnet.ConnectedProcessor != null).ToArray());
        }

        /// <summary>
        /// Respond to one single input character in Unicode, using the pretend
        /// virtual Unicode terminal keycodes described in the UnicodeCommand enum
        /// (see <see cref="TerminalInputRouter{TClient}.ProcessOneInputChar"/>, which does the work).
        /// <br/>
        /// This method is public because it is also how other mods should send input
        /// to the terminal if they want some other source to send simulated keystrokes.
        /// </summary>
        /// <param name="ch">The character, which might be a UnicodeCommand char</param>
        /// <param name="whichTelnet">If this came from a telnet session, which one did it come from?
        /// Set to null in order to say it wasn't from a telnet but was from the interactive GUI</param>
        /// <param name="allowQueue">true if the keypress should get queued if we're not ready for it
        /// right now.  If false, then the keypress will be ignored if we're not ready for it.</param>
        /// <param name="forceQueue">true if the keypress MUST get queued even if we are ready for it.
        /// Use this for input that can jam in quickly faster than the interpeter could respond, to ensure it
        /// doesn't get the order mixed up.  (I.e. use it for paste buffer dumps or telnet input, but not
        /// live GUI typed stuff.)</param>
        /// <returns>True if the input got consuemed or enqueued.  If the input was blocked and not ignored, it returns false.</returns>
        public bool ProcessOneInputChar(char ch, TelnetSingletonServer whichTelnet, bool allowQueue, bool forceQueue)
        {
            return inputRouter.ProcessOneInputChar(ch, whichTelnet, allowQueue, forceQueue);
        }

        /// <summary>
        /// This is identical to calling ProcessOneInputChar with forceQueue defaulted to true,
        /// and it returns void instead of bool.
        /// <para>This is being done this way because it has to match exactly to how the
        /// signature of the method used to look, to keep it compatible with the DLL for
        /// kOSPropMonitor without kOSPropMonitor being recompiled.</para>
        /// </summary>
        /// <param name="ch"></param>
        /// <param name="whichTelnet"></param>
        /// <param name="allowQueue"></param>
        /// <returns></returns>
        public void ProcessOneInputChar(char ch, TelnetSingletonServer whichTelnet, bool allowQueue)
        {
            ProcessOneInputChar(ch, whichTelnet, allowQueue, true);
        }

        /// <summary>
        /// This is identical to calling ProcessOneInputChar with allowQueu and forceQueue both defaulted to true,
        /// and it reutrns void instead of bool.
        /// <para>This is being done this way because it has to match exactly to how the
        /// signature of the method used to look, to keep it compatible with the DLL for
        /// kOSPropMonitor without kOSPropMonitor being recompiled.</para>
        /// </summary>
        /// <param name="ch"></param>
        /// <param name="whichTelnet"></param>
        /// <param name="allowQueue"></param>
        /// <returns></returns>
        public void ProcessOneInputChar(char ch, TelnetSingletonServer whichTelnet)
        {
            ProcessOneInputChar(ch, whichTelnet, true, true);
        }

        /// <summary>
        /// When the input queue is not empty and the interpreter is back at the prompt,
        /// type the queued keys there (see <see cref="TerminalInputRouter{TClient}.ProcessUnconsumedInput"/>).
        /// </summary>
        void ProcessUnconsumedInput()
        {
            inputRouter.ProcessUnconsumedInput();
        }

        IScreenBuffer ITerminalInputHost<TelnetSingletonServer>.Screen
        {
            get { return shared == null ? null : shared.Screen; }
        }

        IInterpreter ITerminalInputHost<TelnetSingletonServer>.Interpreter
        {
            get { return shared == null ? null : shared.Interpreter; }
        }

        bool ITerminalInputHost<TelnetSingletonServer>.HasBooted
        {
            get { return shared.Processor.HasBooted; }
        }

        bool ITerminalInputHost<TelnetSingletonServer>.IsClientAttached(TelnetSingletonServer client)
        {
            return client.ConnectedProcessor != null && session.HasViewer(client);
        }

        bool ITerminalInputHost<TelnetSingletonServer>.TryReadClientChar(TelnetSingletonServer client, bool includeText, out char ch)
        {
            return client.TryReadChar(includeText, out ch);
        }

        void ITerminalInputHost<TelnetSingletonServer>.CloseWindow()
        {
            Close();
        }

        void ITerminalInputHost<TelnetSingletonServer>.DetachClient(TelnetSingletonServer client)
        {
            DetachTelnet(client);
        }

        void ITerminalInputHost<TelnetSingletonServer>.RepaintClient(TelnetSingletonServer client)
        {
            session.RepaintViewer(client);
        }

        void ITerminalInputHost<TelnetSingletonServer>.KeyClick()
        {
            // A mod option since Phase 4 step 3 (it was a toggle in each window); read on each key, so it applies at once:
            if (IsOpen && kOS.Suffixed.Config.Instance.TerminalKeyClick)
                shared.SoundMaker.BeginFileSound("click");
        }

        void TerminalGui(int windowId)
        {
            // Under everything, the fallback screens too (no window skin draws a background):
            KOSStyle.Fill(new Rect(0, 0, WindowRect.width, WindowRect.height), KOSStyle.WindowBackground);
            if (!renderFaultRecovery.IsFaulted)
                BeginResizeAtEdge(); // before the selection and DragWindow see the press
            ScrollViewWithWheel();
            SelectWithMouse(); // before DragWindow sees the drag
            Color previousColor = GUI.color;
            GUISkin previousSkin = GUI.skin;
            try
            {
                if (renderFaultRecovery.IsFaulted ||
                    !renderFaultRecovery.TryRender(RenderTimeSeconds(), DrawTerminalGui, CanRecoverRenderFault, ReportRenderFault))
                {
                    GUI.color = color;
                    GUI.skin = HighLogic.Skin;
                    DrawRenderFaultFallback();
                }
            }
            finally
            {
                GUI.color = previousColor;
                GUI.skin = previousSkin;
            }
        }

        /// <summary>
        /// The mouse wheel over the characters moves the shared view, as PageUp/PageDown do
        /// (Phase 3, O1), also while a program runs (those keys then wait in the type-ahead).
        /// Hovering already locks the camera controls (INPUT-01), so the camera does not zoom.
        /// </summary>
        private void ScrollViewWithWheel()
        {
            Event current = Event.current;
            if (current == null || current.type != EventType.ScrollWheel) return;
            if (shared == null || shared.Screen == null) return;
            // Between the header and the strip: the characters, their padding and the gutter.
            if (!ToRect(Layout.Body(WindowRect.width, WindowRect.height)).Contains(current.mousePosition)) return;
            int lines = wheelScroll.Lines(current.delta.y);
            if (lines != 0)
                shared.Screen.ScrollVertical(lines);
            current.Use();
        }

        /// <summary>The character field in window coordinates, as <c>DrawTerminalGui</c> lays it out.</summary>
        private Rect CharacterField(IScreenBuffer screen)
        {
            return ToRect(Layout.Field(screen.ColumnCount, screen.RowCount));
        }

        /// <summary>
        /// The chrome's layout for the current UI scale and cell size (Phase 4), made again
        /// when either changes.
        /// </summary>
        private TerminalChromeLayout Layout
        {
            get
            {
                IScreenBuffer screen = shared == null ? null : shared.Screen;
                int cellWidth = screen == null ? 0 : Math.Max(0, screen.CharacterPixelWidth);
                int cellHeight = screen == null ? 0 : Math.Max(0, screen.CharacterPixelHeight);
                float scale = TerminalChromeLayout.ClampScale(KOSStyle.UiScale);
                if (layout == null || layout.Scale != scale || layout.CellWidth != cellWidth || layout.CellHeight != cellHeight)
                    layout = new TerminalChromeLayout(scale, cellWidth, cellHeight);
                return layout;
            }
        }

        private static Rect ToRect(ScreenRect rect)
        {
            return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        /// <summary>
        /// Mouse selection (Phase 3, O3): button 0 down over the characters or their padding
        /// starts one (a click alone selects nothing), a drag moves its end to the nearest cell boundary,
        /// and while the pointer is above or below the field the view scrolls a line every
        /// 50 ms. The events are used, so the window does not move. Keyboard focus is taken
        /// in <c>KOSManagedWindow</c> as before.
        /// </summary>
        private void SelectWithMouse()
        {
            // Asked on every event, so the id is the same in each; the drag holds it as the
            // hot control, which keeps the mouse events coming while outside the window.
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            Event current = Event.current;
            if (current == null || shared == null || shared.Screen == null) return;
            IScreenBuffer screen = shared.Screen;
            if (screen.CharacterPixelWidth <= 0 || screen.CharacterPixelHeight <= 0) return;
            Rect field = CharacterField(screen);
            switch (current.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (current.button != 0 ||
                        !ToRect(Layout.SelectionArea(WindowRect.width, WindowRect.height)).Contains(current.mousePosition)) return;
                    lastDragPosition = current.mousePosition;
                    int row, column;
                    CellBoundaryAt(screen, field, current.mousePosition, out row, out column);
                    selection.Begin(row, column);
                    GUIUtility.hotControl = controlId;
                    current.Use();
                    break;
                case EventType.MouseDrag:
                    if (!selection.IsDragging) return;
                    lastDragPosition = current.mousePosition;
                    ExtendSelection(screen, field);
                    current.Use();
                    break;
                case EventType.MouseUp:
                    if (!selection.IsDragging) return;
                    selection.EndDrag();
                    if (GUIUtility.hotControl == controlId)
                        GUIUtility.hotControl = 0;
                    current.Use();
                    break;
                case EventType.Repaint:
                    if (selection.IsDragging && (lastDragPosition.y < field.yMin || lastDragPosition.y >= field.yMax) &&
                        DateTime.Now >= nextSelectionScroll)
                    {
                        nextSelectionScroll = DateTime.Now + selectionScrollInterval;
                        screen.ScrollVertical(lastDragPosition.y < field.yMin ? -1 : 1);
                        ExtendSelection(screen, field);
                    }
                    break;
            }
        }

        private void ExtendSelection(IScreenBuffer screen, Rect field)
        {
            int row, column;
            CellBoundaryAt(screen, field, lastDragPosition, out row, out column);
            selection.DragTo(row, column);
        }

        /// <summary>
        /// The cell boundary nearest a point: its row (as <c>TopRow</c> counts) and the
        /// boundary column (0 to the column count). Above the field is the top row's start,
        /// below it the bottom row's end.
        /// </summary>
        private static void CellBoundaryAt(IScreenBuffer screen, Rect field, Vector2 point, out int row, out int column)
        {
            int viewRow = (int)Math.Floor((point.y - field.yMin) / screen.CharacterPixelHeight);
            column = (int)Math.Round((point.x - field.xMin) / screen.CharacterPixelWidth);
            column = Math.Max(0, Math.Min(column, screen.ColumnCount));
            if (viewRow < 0)
            {
                viewRow = 0;
                column = 0;
            }
            else if (viewRow >= screen.RowCount)
            {
                viewRow = screen.RowCount - 1;
                column = screen.ColumnCount;
            }
            row = screen.TopRow + viewRow;
        }

        private static double RenderTimeSeconds()
        {
            return (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
        }

        private void PrepareTerminalRendering()
        {
            GetFontIfChanged(true);
            if (shared.Screen.CharacterPixelWidth <= 0 || shared.Screen.CharacterPixelHeight <= 0)
                throw new InvalidOperationException("Terminal character dimensions must be positive before rendering.");
            // KSP's UI scale changed: the chrome follows it and the cells stay, so the window's size changes.
            if (TerminalChromeLayout.ClampScale(KOSStyle.UiScale) != fittedScale)
                FitWindowToScreen(shared.Screen);
        }

        private static bool CanRecoverRenderFault(Exception exception)
        {
            // ExitGUI is intentional Unity control flow, sometimes reflection-wrapped.
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (current is ExitGUIException) return false;
                var aggregate = current as AggregateException;
                if (aggregate != null)
                    foreach (Exception inner in aggregate.InnerExceptions)
                        if (!CanRecoverRenderFault(inner)) return false;
            }
            return true; // The safe gate additionally excludes fatal runtime exceptions.
        }

        private void ReportRenderFault(Exception exception, int suppressed)
        {
            // Do not print into the potentially faulty screen or append script-error history.
            UnityEngine.Debug.LogError(string.Format("kOS: Terminal {0} display fault; {1} failed attempts suppressed. Retrying; CPU execution unchanged.", UniqueId, suppressed), this);
            UnityEngine.Debug.LogException(exception, this);
        }

        private void DrawRenderFaultFallback()
        {
            GUI.Label(new Rect(15, 35, Math.Max(1, WindowRect.width - 30), Math.Max(1, WindowRect.height - 100)),
                "Terminal display fault.\nSee KSP.log; retrying automatically.\nCPU execution is unchanged.");
            if (GUI.Button(new Rect(15, WindowRect.height - 30, 65, 25), "Retry"))
                renderFaultRecovery.RequestRetry();
            if (GUI.Button(new Rect(WindowRect.width - 75, WindowRect.height - 30, 50, 25), "Close"))
            {
                Close();
                Event.current.Use();
                return;
            }
            if (GUI.RepeatButton(new Rect(WindowRect.width - 70, WindowRect.height - 65, 65, 25), "Resize") && !resizeMouseDown)
            {
                resizeMouseDown = true;
                resizeOldSize = new Vector2(WindowRect.width, WindowRect.height);
            }
            fontGotResized = false;
            CheckFaultResizeDrag(); // Pixel-only here; model resize belongs inside the guarded retry.
            GUI.DragWindow();
        }

        private void DrawTerminalGui()
        {
            if (missingInstallFiles.Count > 0)
            {
                GUI.Label(new Rect(15, 15, 450, 300),
                    "Error: kOS is not installed where it should be.\n" +
                    "These files were not found in your KSP folder:\n\n" +
                    string.Join("\n", missingInstallFiles.ToArray()) + "\n\n" +
                    "The kOS folder must be directly in GameData.\n" +
                    "Please try installing kOS again.");

                if (GUI.Button(new Rect(WindowRect.width - 75, WindowRect.height - 30, 50, 25), "Close"))
                {
                    Close();
                    Event.current.Use();
                }
                return;
            }

            if (shared == null || shared.Screen == null)
            {
                return;
            }
            IScreenBuffer screen = shared.Screen;
            if (resizeFromFault)
            {
                screen.SetSize(HowManyRowsFit(), HowManyColumnsFit());
                resizeFromFault = false;
            }

            fontGotResized = false;
            if (formerCharPixelWidth != screen.CharacterPixelWidth || formerCharPixelHeight != screen.CharacterPixelHeight)
            {
                formerCharPixelWidth = screen.CharacterPixelWidth;
                formerCharPixelHeight = screen.CharacterPixelHeight;
                screen.SetSize(HowManyRowsFit(), HowManyColumnsFit());
                fontGotResized = true;
            }

            // After a resize above, so the parts are laid out for the window's new size:
            TerminalChromeLayout chrome = Layout;
            float width = WindowRect.width, height = WindowRect.height;
            DrawCharacters(screen, chrome, width, height);
            DrawScrollBar(screen, chrome, width, height);
            DrawHeader(screen, chrome, width);
            DrawStrip(screen, chrome, width, height);
            // The frame, over everything: the accent color says the terminal has the keyboard.
            KOSStyle.Frame(new Rect(0, 0, width, height), chrome.Border, HasKeyboard ? KOSStyle.Accent : KOSStyle.FrameColor);
            DrawResizeFeedback(chrome, width, height);

            // The window moves by its header and the strip, after their buttons took their own
            // clicks; a drag over the characters selects (O3).
            GUI.DragWindow(ToRect(chrome.Header(width)));
            GUI.DragWindow(ToRect(chrome.Strip(width, height)));
        }

        /// <summary>
        /// The characters on the body's background, which is the text color while the screen
        /// is reversed (reverse screen, or a visual beep), with the selection and the cursor.
        /// </summary>
        private void DrawCharacters(IScreenBuffer screen, TerminalChromeLayout chrome, float width, float height)
        {
            int charHeight = screen.CharacterPixelHeight;
            int charWidth = screen.CharacterPixelWidth;

            currentTextColor = IsPowered ? textColor : textColorOff;
                        
            // Paint the background color.
            DateTime nowTime = DateTime.Now;
            bool reversingScreen = (nowTime > blinkEndTime) ? screen.ReverseScreen : (!screen.ReverseScreen);
            Rect body = ToRect(chrome.Body(width, height));
            KOSStyle.Fill(body, bgColor);
            if (reversingScreen)
            {   // In reverse screen mode, draw a big rectangle in foreground color across the whole active screen area:
                KOSStyle.Fill(body, AdjustColor(textColor, screen.Brightness));
            }
            Rect field = CharacterField(screen);
            Color previousTextColor = terminalLetterSkin.label.normal.textColor;
            // +4 so descenders and underscores visible on bottom row; within the body, so that while
            // a resize drag shrinks the window the old rows do not run over the strip and gutter.
            GUI.BeginGroup(new Rect(field.x, field.y, Math.Max(0, Math.Min(field.width + 2, body.xMax - field.x)),
                Math.Max(0, Math.Min(field.height + 4, body.yMax - field.y))));
            try
            {
                terminalLetterSkin.label.normal.textColor = AdjustColor(reversingScreen ? bgColor : currentTextColor, screen.Brightness);

                // A quicksave can leave the snapshot null. Capture one snapshot for both
                // row content and cursor coordinates; live coordinates may already differ.
                IScreenSnapShot snapshot = session.Snapshot;
                if (snapshot != null && snapshot.Buffer != null)
                {
                    List<IScreenBufferLine> buffer = snapshot.Buffer;
                    int rowsToPaint = Math.Min(screen.RowCount, Math.Min(snapshot.RowCount, buffer.Count));

                    for (int row = 0; row < rowsToPaint; ++row)
                    {
                        IScreenBufferLine line = buffer[row];
                        if (line == null) continue; // Treat an unavailable row as blank.
                        // Null cells shown as spaces, matching PRINT AT; a row's string is kept while it shows the same (P-08):
                        GUI.Label(new Rect(0, (row * charHeight), WindowRect.width - 10, charHeight), rowTexts.Get(row, line), terminalLetterSkin.label);
                    }
                    rowTexts.Keep(rowsToPaint);
                    DrawSelection(snapshot.TopRow, rowsToPaint, screen.ColumnCount, charWidth, charHeight,
                        reversingScreen, screen.Brightness);

                    char ch;
                    if (cursorBlinkTime < 0.5f && IsPowered && ShowCursor &&
                        snapshot.TryGetCursorCharacter(screen.RowCount, screen.ColumnCount, out ch))
                    {
                        DrawCursorAt(ch, snapshot.CursorColumn, snapshot.CursorRow, reversingScreen,
                                     charWidth, charHeight, screen.Brightness);
                    }
                }
            }
            finally
            {
                // Always unwind the clipping stack, even if restoring style state fails.
                try
                {
                    terminalLetterSkin.label.normal.textColor = previousTextColor;
                    GUI.color = color;
                }
                finally { GUI.EndGroup(); }
            }
        }

        private static int IconSize(TerminalChromeLayout chrome)
        {
            return Math.Max(6, (int)Math.Round(chrome.HeaderHeight * 0.6));
        }

        /// <summary>
        /// The header (Phase 4): the terminal's name, then the smaller and larger text buttons
        /// (step 3: a small and a large A; <c>CHARHEIGHT</c> in steps of 2, dim at the end of its
        /// range; saved with the geometry) and the close button.
        /// </summary>
        private void DrawHeader(IScreenBuffer screen, TerminalChromeLayout chrome, float width)
        {
            KOSStyle.Fill(ToRect(chrome.Header(width)), KOSStyle.ChromeBackground);
            titleLabel = KOSStyle.TitleLabel(chrome.FontSize);
            if (measureTitleText == null)
                measureTitleText = text => KOSStyle.Measure(titleLabel, text);
            Rect title = ToRect(chrome.Title(width));
            GUI.Label(title, shownTitle.Get(headerTitle, title.width, titleLabel, measureTitleText, KOSStyle.Ellipsis), titleLabel);

            int charHeight = postponedCharPixelHeight >= 0 ? postponedCharPixelHeight : screen.CharacterPixelHeight;
            int smaller = TerminalChromeLayout.SmallerCharacterHeight(charHeight);
            int larger = TerminalChromeLayout.LargerCharacterHeight(charHeight);
            // A small and a large A; applied at the next OnGUI, between passes:
            if (KOSStyle.TextButton(ToRect(chrome.SmallerTextButton(width)), "A", Math.Max(6, (int)Math.Round(chrome.FontSize * 0.8)), smaller != charHeight))
                postponedCharPixelHeight = smaller;
            if (KOSStyle.TextButton(ToRect(chrome.LargerTextButton(width)), "A", (int)Math.Round(chrome.FontSize * 1.35), larger != charHeight))
                postponedCharPixelHeight = larger;
            if (KOSStyle.IconButton(ToRect(chrome.CloseButton(width)), KOSStyle.Icon.Close, IconSize(chrome)))
            {
                Close();
                Event.current.Use();
            }
        }

        /// <summary>
        /// The status strip (Phase 4; replaces the bottom bar): the state after a dot of its
        /// color, the chips that apply, a copy notice and the size, placed by
        /// <see cref="StatusStrip"/> so that nothing overlaps (BAR-01); the resize grip at its
        /// right end. Clicking <c>New output</c> brings the view back to the screen.
        /// </summary>
        private void DrawStrip(IScreenBuffer screen, TerminalChromeLayout chrome, float width, float height)
        {
            Rect strip = ToRect(chrome.Strip(width, height));
            KOSStyle.Fill(strip, KOSStyle.ChromeBackground);
            GUIStyle label = KOSStyle.Label(chrome.FontSize);
            stripLabel = label;
            if (measureStripText == null)
                measureStripText = text => KOSStyle.Measure(stripLabel, text);
            int dot = Math.Max(4, (int)Math.Round(chrome.FontSize * 0.55));
            float dotWidth = dot + Mathf.Round(dot * 0.7f);

            TerminalStatus shown = status;
            string notice = copyNotice != null && DateTime.Now < copyNoticeEnd ? copyNotice : null;
            ScreenRect content = chrome.StripContent(width, height);
            float chipInset = Math.Max(1, Mathf.Round(strip.height * 0.15f));
            var newOutputChip = new Rect(0, 0, 0, 0);
            // Laid out again only when something it shows changed (P-07):
            List<StatusStrip.Placement> parts = stripCache.Layout(label, measureStripText, chrome.Padding, chrome.Padding, dotWidth,
                KOSStyle.Ellipsis, content.X, content.Width, shown, notice, screen.ColumnCount, screen.RowCount);
            List<StatusChip> chips = stripCache.Chips;
            foreach (StatusStrip.Placement part in parts)
            {
                var where = new Rect(part.X, strip.y, part.Width, strip.height);
                switch (part.Part)
                {
                    case StatusStrip.Part.State:
                        KOSStyle.DrawIcon(new Rect(where.x, where.y, dot, where.height), KOSStyle.Icon.Dot, dot, KOSStyle.StatusColor(shown.Kind));
                        KOSStyle.Text(new Rect(where.x + dotWidth, where.y, Math.Max(0, where.width - dotWidth), where.height), part.Text, label, KOSStyle.ChromeText);
                        break;
                    case StatusStrip.Part.Chip:
                        StatusChip chip = chips[part.ChipIndex];
                        var background = new Rect(where.x, where.y + chipInset, where.width, Math.Max(0, where.height - 2 * chipInset));
                        KOSStyle.Fill(background, KOSStyle.ChipBackground);
                        Color chipColor = KOSStyle.ChromeText;
                        if (chip.Kind == StatusChipKind.NoConnection)
                            chipColor = KOSStyle.Warning;
                        else if (chip.Kind == StatusChipKind.NewOutput)
                        {
                            chipColor = KOSStyle.Accent;
                            newOutputChip = background;
                            if (background.Contains(Event.current.mousePosition))
                                KOSStyle.Fill(background, KOSStyle.Hover);
                        }
                        KOSStyle.Text(background, part.Text, KOSStyle.CenteredLabel(chrome.FontSize), chipColor);
                        break;
                    case StatusStrip.Part.Notice:
                        KOSStyle.Text(where, part.Text, label, KOSStyle.Accent);
                        break;
                    case StatusStrip.Part.Size:
                        KOSStyle.Text(where, part.Text, label, KOSStyle.DimText);
                        break;
                }
            }
            // Asked for on every event, chip or not, so the controls after it keep their ids:
            if (GUI.Button(newOutputChip, GUIContent.none, GUIStyle.none) && newOutputChip.width > 0)
                ShowNewOutput(screen);

            // A press on the grip is the bottom-right corner's (BeginResizeAtEdge):
            KOSStyle.DrawIcon(ToRect(chrome.Grip(width, height)), KOSStyle.Icon.Grip, IconSize(chrome), KOSStyle.DimText);
        }

        /// <summary>The <c>New output</c> chip was clicked: the view goes back to the screen, as typing brings it.</summary>
        private void ShowNewOutput(IScreenBuffer screen)
        {
            var screenBuffer = screen as ScreenBuffer;
            if (screenBuffer != null && screenBuffer.ViewOffset > 0)
                screenBuffer.ScrollVertical(screenBuffer.ViewOffset);
            nextStatusTime = DateTime.MinValue; // the chip goes at the next update
        }

        protected Color AdjustColor(Color baseColor, double brightness)
        {
            Color newColor = baseColor;
            newColor.a = Convert.ToSingle(brightness); // represent dimness by making it fade into the backround.
            return newColor;
        }

        private static Vector2 MouseOnScreen()
        {
            return new Vector2(Input.mousePosition.x, UnityEngine.Screen.height - Input.mousePosition.y);
        }

        private static ScreenRect ToScreenRect(Rect rect)
        {
            return new ScreenRect(rect.x, rect.y, rect.width, rect.height);
        }

        private bool ScrollBarShown()
        {
            var buffer = shared == null ? null : shared.Screen as ScreenBuffer;
            return buffer != null && TerminalScrollBar.IsShown(buffer.ViewOffset, scrollDragging);
        }

        /// <summary>The edges a press at <paramref name="point"/> (window coordinates) would resize.</summary>
        private ResizeEdges EdgesUnder(Vector2 point)
        {
            return TerminalResize.EdgesAt(Layout, WindowRect.width, WindowRect.height, point.x, point.y, ScrollBarShown());
        }

        /// <summary>
        /// A press on the frame's band, a corner or the grip starts a resize drag (Phase 4, step 2),
        /// followed in <see cref="FollowResizeDrag"/>. The press is used, so the selection and
        /// DragWindow do not see it.
        /// </summary>
        private void BeginResizeAtEdge()
        {
            Event current = Event.current;
            if (current == null || current.type != EventType.MouseDown || current.button != 0) return;
            if (shared == null || shared.Screen == null || resizeEdges != ResizeEdges.None) return;
            ResizeEdges edges = EdgesUnder(current.mousePosition);
            if (edges == ResizeEdges.None) return;
            resizeEdges = edges;
            resizeStartRect = WindowRect;
            resizeStartMouse = MouseOnScreen();
            resizeStartColumns = shared.Screen.ColumnCount;
            resizeStartRows = shared.Screen.RowCount;
            resizeTarget = new ResizeResult(ToScreenRect(WindowRect), resizeStartColumns, resizeStartRows);
            current.Use();
        }

        /// <summary>
        /// While a resize drag goes on, the window takes the size of the nearest whole cells,
        /// from the mouse's position on the screen (not the window's events, which move with the
        /// window). On release the screen takes the new size once, so scripts' resize watchers
        /// and telnet clients see one resize. Called from OnGUI before the window is drawn.
        /// </summary>
        private void FollowResizeDrag()
        {
            if (resizeEdges == ResizeEdges.None) return;
            if (shared == null || shared.Screen == null)
            {
                resizeEdges = ResizeEdges.None;
                return;
            }
            if (Input.GetMouseButton(0))
            {
                Vector2 delta = MouseOnScreen() - resizeStartMouse;
                resizeTarget = TerminalResize.Drag(Layout, ToScreenRect(resizeStartRect), resizeStartColumns, resizeStartRows,
                    resizeEdges, delta.x, delta.y, UnityEngine.Screen.width, UnityEngine.Screen.height);
                WindowRect = ToRect(resizeTarget.Window);
                keeper.SizedByUser(resizeTarget.Window);
                return;
            }
            resizeEdges = ResizeEdges.None;
            shared.Screen.SetSize(resizeTarget.Rows, resizeTarget.Columns);
            FitWindowToScreen(shared.Screen); // also when the cells did not change (SetSize then does not notify)
            keeper.SizedByUser(ToScreenRect(WindowRect));
        }

        /// <summary>
        /// The window kept on screen, its size kept (step 2, Q2): wholly after a restore, an
        /// open, a change of resolution, UI scale or size; while moved, by its header (DRAG-01).
        /// </summary>
        private void KeepOnScreen()
        {
            TerminalChromeLayout chrome = Layout;
            float reachable = (float)Math.Round(WindowPlacement.BASE_REACHABLE_HEADER * chrome.Scale);
            WindowRect = ToRect(keeper.Keep(ToScreenRect(WindowRect), chrome.HeaderButtons(WindowRect.width).X, chrome.HeaderHeight,
                reachable, UnityEngine.Screen.width, UnityEngine.Screen.height, chrome.Scale));
        }

        /// <summary>
        /// Over the frame: the edges a press would resize, or are resizing, in the accent color;
        /// during a resize drag, the size it gives in the middle of the body.
        /// </summary>
        private void DrawResizeFeedback(TerminalChromeLayout chrome, float width, float height)
        {
            Event current = Event.current;
            if (current == null || current.type != EventType.Repaint) return;
            ResizeEdges edges = resizeEdges;
            if (edges == ResizeEdges.None && !Input.GetMouseButton(0) && !selection.IsDragging && !scrollDragging &&
                IsInsideMyExposedPortion(MouseOnScreen()))
                edges = EdgesUnder(current.mousePosition);
            float thickness = Math.Max(2, 2 * chrome.Border);
            if ((edges & ResizeEdges.Left) != 0)
                KOSStyle.Fill(new Rect(0, 0, thickness, height), KOSStyle.Accent);
            if ((edges & ResizeEdges.Right) != 0)
                KOSStyle.Fill(new Rect(width - thickness, 0, thickness, height), KOSStyle.Accent);
            if ((edges & ResizeEdges.Top) != 0)
                KOSStyle.Fill(new Rect(0, 0, width, thickness), KOSStyle.Accent);
            if ((edges & ResizeEdges.Bottom) != 0)
                KOSStyle.Fill(new Rect(0, height - thickness, width, thickness), KOSStyle.Accent);

            if (resizeEdges == ResizeEdges.None) return;
            string size = resizeTarget.Columns + "x" + resizeTarget.Rows; // as the strip shows it
            GUIStyle label = KOSStyle.CenteredLabel(chrome.FontSize);
            float boxWidth = Mathf.Round(KOSStyle.Measure(label, size) + 4 * chrome.Padding);
            float boxHeight = chrome.HeaderHeight;
            Rect body = ToRect(chrome.Body(width, height));
            var box = new Rect(Mathf.Round(body.center.x - boxWidth / 2f), Mathf.Round(body.center.y - boxHeight / 2f), boxWidth, boxHeight);
            KOSStyle.Fill(box, KOSStyle.ChromeBackground);
            KOSStyle.Frame(box, chrome.Border, KOSStyle.Accent);
            KOSStyle.Text(box, size, label, KOSStyle.ChromeText);
        }

        /// <summary>
        /// The scroll bar (Phase 4, step 2), in the gutter while the view is scrolled back: the
        /// thumb drags the shared view (telnet clients follow it, as with the wheel), a press
        /// above or below it moves a screen's rows. Back at the bottom it goes once released.
        /// </summary>
        private void DrawScrollBar(IScreenBuffer screen, TerminalChromeLayout chrome, float width, float height)
        {
            // Asked on every event, so the id is the same in each; the drag holds it as the hot control.
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            var buffer = screen as ScreenBuffer;
            Event current = Event.current;
            if (buffer == null || current == null || !TerminalScrollBar.IsShown(buffer.ViewOffset, scrollDragging)) return;
            ScreenRect track = chrome.ScrollTrack(width, height);
            ScreenRect thumb = TerminalScrollBar.Thumb(track, buffer.ScrollbackRows, buffer.RowCount, buffer.ViewOffset, chrome.MinimumThumb);
            switch (current.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (current.button != 0 || !ToRect(chrome.Gutter(width, height)).Contains(current.mousePosition)) return;
                    int side = TerminalScrollBar.SideOfThumb(thumb, current.mousePosition.y);
                    if (side == 0)
                    {
                        scrollDragging = true;
                        scrollGrabOffset = current.mousePosition.y - thumb.Y;
                        GUIUtility.hotControl = controlId;
                    }
                    else
                    {
                        screen.ScrollVertical(side * screen.RowCount); // a page
                    }
                    current.Use();
                    break;
                case EventType.MouseDrag:
                    if (!scrollDragging) return;
                    int offset = TerminalScrollBar.ViewOffsetFor(track, buffer.ScrollbackRows, buffer.RowCount, chrome.MinimumThumb,
                        current.mousePosition.y - scrollGrabOffset, buffer.ViewOffset);
                    if (offset != buffer.ViewOffset)
                        screen.ScrollVertical(buffer.ViewOffset - offset);
                    current.Use();
                    break;
                case EventType.MouseUp:
                    if (!scrollDragging) return;
                    scrollDragging = false;
                    if (GUIUtility.hotControl == controlId)
                        GUIUtility.hotControl = 0;
                    current.Use();
                    break;
                case EventType.Repaint:
                    Rect thumbRect = new Rect(track.X, Mathf.Round(thumb.Y), track.Width, Mathf.Round(thumb.Height));
                    KOSStyle.Fill(ToRect(track), KOSStyle.ScrollTrack);
                    KOSStyle.Fill(thumbRect, scrollDragging || thumbRect.Contains(current.mousePosition) ? KOSStyle.ChromeText : KOSStyle.DimText);
                    break;
            }
        }

        /// <summary>The window as it is now, to save (step 2).</summary>
        private TerminalGeometry CurrentGeometry()
        {
            IScreenBuffer screen = shared.Screen;
            return new TerminalGeometry
            {
                X = (int)Math.Round(WindowRect.x),
                Y = (int)Math.Round(WindowRect.y),
                Columns = screen.ColumnCount,
                Rows = screen.RowCount,
                CharacterHeight = screen.CharacterPixelHeight
            };
        }

        /// <summary>
        /// Save the geometry if it changed: looked at every <see cref="geometryCheckInterval"/>,
        /// saved at most every <see cref="geometrySaveInterval"/>, not while the mouse button is
        /// held (a move or a resize in progress). <paramref name="now"/> skips those waits.
        /// Every change counts: moving, resizing, the character size, a script's TERMINAL suffixes.
        /// </summary>
        private void SaveGeometryIfChanged(bool now)
        {
            if (geometryStore == null || shared == null || shared.Screen == null) return;
            DateTime time = DateTime.Now;
            if (!now && (time < nextGeometryCheck || resizeEdges != ResizeEdges.None || Input.GetMouseButton(0))) return;
            nextGeometryCheck = time + geometryCheckInterval;
            TerminalGeometry current = CurrentGeometry();
            if (current.Equals(savedGeometry)) return;
            geometryStore.Save(current); // never throws for I/O; a failure is logged once
            savedGeometry = current;
            nextGeometryCheck = time + geometrySaveInterval;
        }

        /// <summary>
        /// The render-fault fallback's resize button (the window's own resizing is the edges':
        /// <see cref="TerminalResize"/>): the window follows the drag in pixels, at least 200 x 200
        /// for its buttons, and on release the guarded retry fits the screen to it. Upstream's
        /// protected <c>CheckResizeDrag()</c>, its resize for the whole window, is gone (review D-01).
        /// </summary>
        private void CheckFaultResizeDrag()
        {
            if (Input.GetMouseButton(0) && !fontGotResized ) // mouse button is maybe dragging the frame
            {
                if (resizeMouseDown) // and it's in the midst of a drag.
                {
                    Vector2 dragDelta = MousePosAbsolute - MouseButtonDownPosAbsolute;
                    WindowRect = new Rect(WindowRect.xMin,
                                          WindowRect.yMin,
                                          Math.Max(resizeOldSize.x + dragDelta.x, 200),
                                          Math.Max(resizeOldSize.y + dragDelta.y, 200));
                }
            }
            else // mouse button is up
            {
                if (resizeMouseDown) // and it had been dragging a resize before.
                {
                    resizeMouseDown = false;
                    // Resize by integer character cells, not by actual x/y pixels:
                    // Note I wanted to call this dynamically as it's resizing, but there's some weird issue
                    // with the timing of it, where the windowRect is temporarily set to a bogus value during the
                    // time it was trying to run this, and thus it created exception-throwing code unless it waits
                    // until the drag is done before trying to calculate this.
                    // The effect the user sees is that the text can float in space past the edge of the window (when
                    // shrinking the size) until the resize drag is done, and then it redraws itself.
                    resizeFromFault = true; // the model resize belongs inside the guarded retry
                    renderFaultRecovery.RequestRetry();
                }
            }
        }
        
        /// <summary>The selection (O3), as a translucent block over the drawn rows.</summary>
        private void DrawSelection(int topRow, int rows, int columns, int charWidth, int charHeight,
            bool reversingScreen, double brightness)
        {
            if (selection.IsEmpty) return;
            Color previousColor = GUI.color;
            try
            {
                Color highlight = reversingScreen ? bgColor : currentTextColor;
                highlight.a = TerminalPalette.SELECTION_ALPHA * Mathf.Clamp01((float)brightness);
                GUI.color = highlight;
                for (int row = 0; row < rows; ++row)
                {
                    int from, to;
                    if (selection.TryGetColumns(topRow + row, columns, out from, out to))
                        GUI.DrawTexture(new Rect(from * charWidth, row * charHeight, (to - from) * charWidth, charHeight),
                            Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
                }
            }
            finally
            {
                GUI.color = previousColor;
            }
        }

        void DrawCursorAt(char ch, int x, int y, bool reversingScreen, int charWidth, int charHeight, double brightness)
        {
            // Draw the solid cursor block and inverted character inside one balanced group.
            Color previousColor = GUI.color;
            Color previousTextColor = terminalLetterSkin.label.normal.textColor;
            GUI.BeginGroup(new Rect((x * charWidth), (y * charHeight), charWidth, charHeight));
            try
            {
                GUI.color = AdjustColor(reversingScreen ? bgColor : cursorColor, brightness); // drawn only while powered
                GUI.DrawTexture(new Rect(0, 0, charWidth, charHeight), Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
                GUI.color = AdjustColor(reversingScreen ? cursorColor : bgColor,
                    2*brightness /*it seems to need slightly higher alpha values to show up atop the solid block*/ );
                terminalLetterSkin.label.normal.textColor = GUI.color;
                GUI.Label(new Rect(0, 0, charWidth, charHeight), ch.ToString(), terminalLetterSkin.label);
            }
            finally
            {
                try
                {
                    terminalLetterSkin.label.normal.textColor = previousTextColor;
                    GUI.color = previousColor;
                }
                finally { GUI.EndGroup(); }
            }
        }

        public Rect GetRect()
        {
            return WindowRect;
        }
        
        public void Print( string str )
        {
            shared.Screen.Print( str );
        }

        internal void AttachTo(SharedObjects sharedObj)
        {
            AttachTo(sharedObj, null);
        }

        /// <param name="geometryStore">where this CPU's window geometry is kept (Phase 4, step 2);
        /// null for none: the window opens at the defaults, as before</param>
        internal void AttachTo(SharedObjects sharedObj, ITerminalGeometryStore geometryStore)
        {
            shared = sharedObj;
            shared.Window = this;
            session.Screen = shared.Screen;

            // The saved geometry, value by value, else the defaults. A boot script that sets the
            // size runs later and wins (D3: the last resize).
            this.geometryStore = geometryStore;
            keeper.FitWhollyNext();
            TerminalGeometry saved = (geometryStore == null ? null : geometryStore.Load()) ?? new TerminalGeometry();
            
            shared.Screen.CharacterPixelWidth = 8; // will be overridden later when drawing the font.
            shared.Screen.CharacterPixelHeight = saved.CharacterHeight ?? SafeHouse.Config.TerminalFontDefaultSize;
            shared.Screen.Brightness = SafeHouse.Config.TerminalBrightness;
            shared.Screen.VisualBeep = kOS.Suffixed.Config.Instance.TerminalVisualBeep; // a mod option since Phase 4 step 3
            formerCharPixelWidth = shared.Screen.CharacterPixelWidth;
            formerCharPixelHeight = shared.Screen.CharacterPixelHeight;
            shared.Screen.SetSize(saved.Rows ?? SafeHouse.Config.TerminalDefaultHeight, saved.Columns ?? SafeHouse.Config.TerminalDefaultWidth);
            WindowRect = new Rect(saved.X ?? WindowRect.x, saved.Y ?? WindowRect.y, WindowRect.width, WindowRect.height); // kept on screen when drawn

            NotifyOfScreenResize(shared.Screen);
            savedGeometry = CurrentGeometry(); // only a change is saved: a window nobody touched leaves no file
            shared.Screen.AddResizeNotifier(NotifyOfScreenResize);
            baseTitle = CalculateTitle();
            headerTitle = CalculateHeaderTitle();
            session.ChangeTitle(baseTitle); // the state follows in the next update

            soundMaker.AttachTo(shared); // Attach the soundMaker also
        }
        
        /// <summary>
        /// Work out the title and the CPU state again (at most every <see cref="statusInterval"/>),
        /// and give telnet clients the title with the state after it; only while someone looks:
        /// the window is open, a telnet client is attached, or the toolbar panel's CPUs tab shows
        /// (P-06). Nobody else needs them, and the first update with someone to look works them
        /// out again (an interval has passed by then, or <see cref="Open"/> asked for it).
        /// </summary>
        private void RefreshStatus()
        {
            if (!IsOpen && session.ViewerCount == 0 && !StatusWanted)
                return; // nobody to show it to
            DateTime now = DateTime.Now;
            if (now < nextStatusTime) return;
            RefreshStatusNow(now);
        }

        private void RefreshStatusNow(DateTime now)
        {
            if (shared == null || shared.Vessel == null || shared.KSPPart == null) return;
            nextStatusTime = now + statusInterval;
            baseTitle = CalculateTitle();
            headerTitle = CalculateHeaderTitle();
            try
            {
                status = DescribeStatus();
                statusText = status.Text;
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                status = null;
                statusText = string.Empty; // the state must never stop the terminal
                if (!reportedStatusFailure)
                {
                    reportedStatusFailure = true;
                    UnityEngine.Debug.LogException(ex, this);
                }
            }
            session.ChangeTitle(TerminalStatus.TitleWithStatus(baseTitle, statusText));
        }

        private TerminalStatus DescribeStatus()
        {
            var status = new TerminalStatus { TelnetClients = session.ViewerCount };
            var processor = shared.Processor as kOSProcessor;
            if (processor != null)
                status.Mode = processor.ProcessorMode;
            status.Booted = shared.Processor != null && shared.Processor.HasBooted;
            if (status.Mode == ProcessorModes.READY && status.Booted)
            {
                // As the input routing does (#2925): only once booted.
                status.AtPrompt = shared.Interpreter != null && shared.Interpreter.IsWaitingForCommand();
                var cpu = shared.Cpu as kOS.Safe.Execution.CPU;
                if (cpu != null)
                {
                    status.ProgramPath = cpu.RunningProgramPath;
                    status.Waiting = waitingDisplay.Update(cpu.SessionTime,
                        cpu.IsWaitingFor(TerminalStatus.WAIT_SHOWN_AFTER_SECONDS), status.AtPrompt, status.ProgramPath);
                }
                var connectivity = shared.Interpreter as ConnectivityInterpreter;
                if (connectivity != null)
                    status.DelaySecondsLeft = connectivity.DelaySecondsLeft;
                status.Connected = kOS.Communication.ConnectivityManager.HasConnectionToControl(shared.Vessel);
                var screenBuffer = shared.Screen as ScreenBuffer;
                status.NewOutput = screenBuffer != null && screenBuffer.HasNewOutputOutOfView;
            }
            else
            {
                waitingDisplay.Reset(); // off, no power or booting: a later wait starts afresh
            }
            return status;
        }

        /// <summary>The telnet title: <c>vessel CPU: CX-4181 (tag)</c>, as upstream.</summary>
        internal string CalculateTitle()
        {
           return String.Format("{0} CPU: {1} ({2})",
                                shared.Vessel.vesselName,
                                CpuNames.PartName(shared.KSPPart.partInfo.title),
                                NameTag() ?? ""
                                );
        }

        /// <summary>
        /// The GUI header's title (Phase 4): <c>vessel · CX-4181 · tag</c>, without the tag when
        /// there is none. Telnet keeps <see cref="CalculateTitle"/>.
        /// </summary>
        private string CalculateHeaderTitle()
        {
            string title = shared.Vessel.vesselName + KOSStyle.Separator + CpuNames.PartName(shared.KSPPart.partInfo.title);
            string tag = NameTag();
            if (!string.IsNullOrEmpty(tag))
                title += KOSStyle.Separator + tag;
            return title;
        }

        /// <summary>The part's name tag; null without a <see cref="KOSNameTag"/> module.</summary>
        private string NameTag()
        {
            KOSNameTag partTag = shared.KSPPart.Modules.OfType<KOSNameTag>().FirstOrDefault();
            return partTag == null ? null : partTag.nameTag;
        }
        
        /// <summary>
        /// A telnet client picked this terminal: the screen takes the client's size, and the
        /// client gets the title and a full repaint.
        /// </summary>
        internal void AttachTelnet(TelnetSingletonServer server)
        {
            // The title it is sent is worked out now: nobody may have looked for a while (P-06).
            RefreshStatusNow(DateTime.Now);
            session.AttachViewer(server);
            nextStatusTime = DateTime.MinValue; // the state again at the next update, counting this client
        }

        internal void DetachTelnet(TelnetSingletonServer server)
        {
            session.DetachViewer(server); // the server's DisconnectFromProcessor() comes back here; it guards that.
        }
        
        public void DetachAllTelnets()
        {
            session.DetachAllViewers();
        }

        internal int NotifyOfScreenResize(IScreenBuffer sb)
        {
            FitWindowToScreen(sb);

            session.ScreenResized(); // telnet clients of another size are asked to follow (decision D3)
            return 0;
        }

        /// <summary>The window's size for the screen's cells, with the chrome at the current UI scale.</summary>
        private void FitWindowToScreen(IScreenBuffer sb)
        {
            var fit = new TerminalChromeLayout(KOSStyle.UiScale, sb.CharacterPixelWidth, sb.CharacterPixelHeight);
            fittedScale = fit.Scale;
            WindowRect = new Rect(WindowRect.xMin, WindowRect.yMin, fit.WindowWidthFor(sb.ColumnCount), fit.WindowHeightFor(sb.RowCount));
        }
        
        public void ClearScreen()
        {
            session.ClearScreen();
        }

        public int NumTelnets()
        {
            return session.ViewerCount;
        }
        
        private int HowManyRowsFit()
        {
            return Layout.RowsThatFit(WindowRect.height);
        }

        private int HowManyColumnsFit()
        {
            return Layout.ColumnsThatFit(WindowRect.width);
        }
        
        private static GUISkin BuildPanelSkin()
        {
            GUISkin theSkin = Instantiate(HighLogic.Skin); // Use Instantiate to make a copy of the Skin Object

            theSkin.label.fontSize = 10;
            theSkin.label.normal.textColor = Color.white;
            theSkin.label.padding = new RectOffset(0, 0, 0, 0);
            theSkin.label.margin = new RectOffset(1, 1, 1, 1);

            theSkin.button.fontSize = 10;
            theSkin.button.padding = new RectOffset(0, 0, 0, 0);
            theSkin.button.margin = new RectOffset(0, 0, 0, 0);

            return theSkin;
        }
    }
}
