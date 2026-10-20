using kOS.Module;
using kOS.Safe.Encapsulation.Suffixes;
using kOS.Safe.Module;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using kOS.Safe.Utilities;
using kOS.UserIO;
using kOS.Utilities;
using KSP.UI.Screens;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace kOS.Screen
{
    /// <summary>
    /// Window that holds the popup that the toolbar button is meant to create.
    /// window.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class KOSToolbarWindow : MonoBehaviour
    {
        private ApplicationLauncherButton launcherButton;
        private IButton blizzyButton;

        private const ApplicationLauncher.AppScenes APP_SCENES =
            ApplicationLauncher.AppScenes.FLIGHT |
            ApplicationLauncher.AppScenes.SPH |
            ApplicationLauncher.AppScenes.VAB |
            ApplicationLauncher.AppScenes.MAPVIEW;

        private static Texture2D launcherButtonTexture;

        // ReSharper disable once RedundantDefaultFieldInitializer
        private bool clickedOn = false;

        private Rect rectToFit = new Rect(0, 0, 1, 1); // will be changed in Open()

        private static Rect windowRect; // does anybody know why this is static?
        private MouseBlocking.Area mouseArea;
        private const int UNIQUE_ID = 8675309; // Jenny, I've got your number.
        private static string versionString;

        ///<summary>Which CPU part description in the gui panel was the mouse hovering over during the current OnGUI call?</summary>
        private Part newHoverPart;

        ///<summary>Which CPU part description was it hovering over prior to the current OnGUI call?</summary>
        private Part prevHoverPart;

        /// <summary>Use this to remember the part's previous highlight color before we messed with it.</summary>
        private Color originalPartHighlightColor = new Color(1.0f, 1.0f, 1.0f); // Should get overwritten with the real color later.

        // This first value is a safety in case we don't do that
        // properly.  We don't want to "restore" the color to null.
        /// <summary>Our highlight color for kOS panel's part highlighting.</summary>
        private readonly Color ourPartHighlightColor = new Color(1.0f, 0.5f, 1.0f); // Bright purple.

        private bool alreadyAwake;
        private bool firstTime = true;
        private bool isOpen;
        private bool wasOpenLastPaint;
        private kOS.Screen.ListPickerDialog fontPicker;
        private kOS.Screen.ListPickerDialog ipAddrPicker;

        private bool uiGloballyHidden = false;


        /// <summary>
        /// Unity hates it when a MonoBehaviour has a constructor,
        /// so all the construction work is here instead:
        /// </summary>
        public static void FirstTimeSetup()
        {
            launcherButtonTexture = Utilities.Utils.GetTextureWithErrorMsg("kOS/GFX/dds_launcher-button", false);
            // The panel's terminal icons (dds_terminal-icon-*) are unused since Phase 4 step 4 (Open/Close buttons).

            windowRect = new Rect(0, 0, 1f, 1f); // this origin point will move when opened/closed.
            versionString = Utils.GetAssemblyFileVersion();
            //UnityEngine.Debug.Log("[kOSToolBarWindow] FirstTimeSetup Finished, v=" + versionString);
        }

        public void Awake()
        {
            // TODO - remove commented-out line below after varifying KSP 1.1 works without it:
            // GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequestedForAppLauncher);

            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Add(RemoveButton);
            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);
            GameEvents.onGameStateSave.Add(OnGameStateSave);
            GameObject.DontDestroyOnLoad(this);

            fontPicker = null;
        }

        // TODO - Remove this next method after verifying KSP 1.1 works without it:
        // private void OnGameSceneLoadRequestedForAppLauncher(GameScenes sceneToLoad)
        // {
        //     GoAway();
        // }

        public void Start()
        {
            // Prevent multiple calls of this:
            if (alreadyAwake) return;
            alreadyAwake = true;
            SafeHouse.Logger.SuperVerbose("[kOSToolBarWindow] Start succesful");
        }

        public void AddButton()
        {
            if (!ApplicationLauncher.Ready) return;

            var useBlizzyOnly = ToolbarManager.ToolbarAvailable &&
                                kOSCustomParameters.Instance != null &&
                                kOSCustomParameters.Instance.useBlizzyToolbarOnly;

            if (firstTime)
            {
                FirstTimeSetup();
                firstTime = false;
            }

            if (!useBlizzyOnly && launcherButton == null)
            {
                ApplicationLauncher launcher = ApplicationLauncher.Instance;

                launcherButton = launcher.AddModApplication(
                    CallbackOnTrue,
                    CallbackOnFalse,
                    CallbackOnHover,
                    CallbackOnHoverOut,
                    CallbackOnEnable,
                    CallbackOnDisable,
                    APP_SCENES,
                    launcherButtonTexture);

                launcher.AddOnShowCallback(CallbackOnShow);
                launcher.AddOnHideCallback(CallbackOnHide);
                launcher.EnableMutuallyExclusive(launcherButton);
            }
            if (blizzyButton == null)
                AddBlizzyButton();

            SetupBackingConfigInts();
            SafeHouse.Logger.SuperVerbose("[kOSToolBarWindow] Launcher Icon init successful");
        }
        
        public void RemoveButton(GameScenes scene)
        {
            RemoveButton();
        }

        public void RemoveButton()
        {
            if (launcherButton != null)
                GoAway();
        }

        public void AddBlizzyButton()
        {
            if (!ToolbarManager.ToolbarAvailable) return;

            blizzyButton = ToolbarManager.Instance.add("kOS", "kOSButton");
            blizzyButton.TexturePath = "kOS/GFX/dds_launcher-button-blizzy";
            blizzyButton.ToolTip = "kOS";
            blizzyButton.OnClick += e => CallbackOnClickBlizzy();
        }

        /// <summary>
        /// In order to support the changes to solve issue #565 (see github for kOS)
        /// we have to store a temp value per integer field, that is NOT the actual
        /// official integer value of the field, but just stores the value the user
        /// is temporarily typing:
        /// </summary>
        public void GoAway()
        {
            if (isOpen) Close();
            clickedOn = false;

            try
            {
                if (launcherButton != null && ApplicationLauncher.Instance != null)
                {
                    ApplicationLauncher launcher = ApplicationLauncher.Instance;

                    launcher.DisableMutuallyExclusive(launcherButton);
                    launcher.RemoveOnRepositionCallback(CallbackOnShow);
                    launcher.RemoveOnHideCallback(CallbackOnHide);
                    launcher.RemoveOnShowCallback(CallbackOnShow);
                    launcher.RemoveModApplication(launcherButton);
                    launcherButton = null;
                }
            }
            catch (Exception e)
            {
                SafeHouse.Logger.SuperVerbose("[kOSToolBarWindow] Failed unregistering AppLauncher handlers," + e.Message);
            }

            // force close the font picker window if it was still open:
            if (fontPicker != null)
            {
                fontPicker.Close();
                Destroy(fontPicker);
                fontPicker = null;
            }
        }

        public void OnDestroy()
        {
            // TODO : Remove the following commented line after it's been discovered that KSP 1.1 works without it:
            // GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequestedForAppLauncher);

            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);
            GameEvents.onGameStateSave.Remove(OnGameStateSave);

            GoAway();
            KOSMouseBlocking.Unregister(ref mouseArea);
            SafeHouse.Logger.SuperVerbose("[kOSToolBarWindow] OnDestroy successful");
        }

        public void CallbackOnClickBlizzy()
        {
            if (!isOpen)
                Open();
            else
                Close();
        }

        /// <summary>Callback for when the button is toggled on</summary>
        public void CallbackOnTrue()
        {
            SafeHouse.Logger.SuperVerbose("KOSToolBarWindow: PROOF: CallbackOnTrue()");
            clickedOn = true;
            Open();
        }

        /// <summary>Callback for when the button is toggled off</summary>
        public void CallbackOnFalse()
        {
            SafeHouse.Logger.SuperVerbose("KOSToolBarWindow: PROOF: CallbackOnFalse()");
            clickedOn = false;
            Close();
        }

        /// <summary>Callback for when the mouse is hovering over the button</summary>
        public void CallbackOnHover()
        {
            SafeHouse.Logger.SuperVerbose("KOSToolBarWindow: PROOF: CallbackOnHover()");
            if (!clickedOn)
                Open();
        }

        /// <summary>Callback for when the mouse is hover is off the button</summary>
        public void CallbackOnHoverOut()
        {
            SafeHouse.Logger.SuperVerbose("KOSToolBarWindow: PROOF: CallbackOnHoverOut()");
            if (!clickedOn)
                Close();
        }

        /// <summary>Callback for when the application launcher shows itself</summary>
        public void CallbackOnShow()
        {
            SafeHouse.Logger.SuperVerbose("KOSToolBarWindow: PROOF: CallbackOnShow()");
            if (clickedOn)
                Open();
        }

        /// <summary>Callback for when the application launcher hides itself</summary>
        public void CallbackOnHide()
        {
            SafeHouse.Logger.SuperVerbose("KOSToolBarWindow: PROOF: CallbackOnHide()");
            Close();
        }

        public void Open()
        {
            SafeHouse.Logger.SuperVerbose("KOSToolBarWindow: PROOF: Open()");

            if (!isOpen)
            {
                tab = PanelTab.Cpus; // the CPUs tab each time the panel opens
                contentScroll = 0;
                scrollDragging = false;
            }

            float assumeStagingListWidth = 64f; // hardcoded for now.  Might try to see how to read it on the fly later.

            bool isTop = ApplicationLauncher.Instance.IsPositionedAtTop;

            if (launcherButton == null)
            {
                if (isTop)
                {
                    rectToFit = new Rect(0, 0, UnityEngine.Screen.width - assumeStagingListWidth, UnityEngine.Screen.height);
                    windowRect = new Rect(UnityEngine.Screen.width, 0, 0, 0);
                }
                else
                {
                    rectToFit = new Rect(0, 0, UnityEngine.Screen.width - assumeStagingListWidth, UnityEngine.Screen.height - assumeStagingListWidth);
                    windowRect = new Rect(UnityEngine.Screen.width, UnityEngine.Screen.height, 0, 0);
                }
                anchorBottom = !isTop;
                isOpen = true;
                return;
            }
            Vector3 launcherScreenCenteredPos = launcherButton.GetAnchorUL();

            // There has *got* to be a method somewhere in Unity that does this transformation
            // without having to hardcode the formula, but after wasting 5 hours searching
            // Unity docs and google and ILSpy, I give up trying to find it.  This formula is
            // probably sitting on top of fragile assumptions, but I give up on trying to find
            // the "right" way.  (The values returned by the  RectTransform appear to be using
            // screen pixel coords, but with the center of the screen being (0,0) rather than
            // one of the corners.  This does not appear to be any of the named reference
            // frames Unity docs talk about ("World", "Viewport", and "Screen")):
            //
            // If any other kOS devs want to try a hand at fighting the Unity docs to figure this
            // out, be my guest.  In the mean time, this is the hardcoded solution:
            float launcherScreenX = launcherScreenCenteredPos.x + UnityEngine.Screen.width / 2;
            float launcherScreenY = launcherScreenCenteredPos.y + UnityEngine.Screen.height / 2;

            // amount to pad on the right side depending on what's there on the screen:

            float fitWidth = (isTop ? launcherScreenX : UnityEngine.Screen.width - assumeStagingListWidth);
            float fitHeight = (isTop ? UnityEngine.Screen.height : UnityEngine.Screen.height - launcherScreenY);

            // subset of the screen we'll clamp to stay within:
            rectToFit = new Rect(0, 0f, fitWidth, fitHeight);

            // Attempt to place the window at first in a position that would extend
            // outside the rectToFit, but at least establishes it at the correct corner
            // of the screen.  Later the auto-layout elsewhere in this class will shift
            // it as needed to obey rectToFit:
            float leftEdge = UnityEngine.Screen.width;
            float topEdge = isTop ? 0f : UnityEngine.Screen.height;

            windowRect = new Rect(leftEdge, topEdge, 0, 0); // sized and placed at the next OnGUI
            anchorBottom = !isTop;

            isOpen = true;
        }

        public void Close()
        {
            SafeHouse.Logger.SuperVerbose("KOSToolBarWindow: PROOF: Close()");
            if (!isOpen)
                return;

            ApplyPendingNumber(); // typing left in a number field counts, as when it loses the keyboard
            ClearPartHighlight();
            TermWindow.StatusWanted = false;
            isOpen = false;
        }

        /// <summary>Callback for when the button is shown or enabled by the application launcher</summary>
        public void CallbackOnEnable()
        {
            SafeHouse.Logger.SuperVerbose("KOSToolBarWindow: PROOF: CallbackOnEnable()");
            // do nothing, but leaving the hook here as a way to document "this thing exists and might be used".
        }

        /// <summary>Callback for when the button is hidden or disabled by the application launcher</summary>
        public void CallbackOnDisable()
        {
            SafeHouse.Logger.SuperVerbose("KOSToolBarWindow: PROOF: CallbackOnDisable()");
            // do nothing, but leaving the hook here as a way to document "this thing exists and might be used".
        }

        void OnHideUI()
        {
            uiGloballyHidden = true;
        }

        void OnShowUI()
        {
            uiGloballyHidden = false;
        }

        // As long as the user can get this toolbar menu dialog to show up, that means
        // they can change a setting in it.  Those changes should be persisted even when
        // there's no KOSProcessor modules loaded in the scene.  (Thus why the config
        // save is done here as well as in KOSProcessor.)
        void OnGameStateSave(ConfigNode node) // ConfigNode ignored.
        {
            if (SafeHouse.Config != null)
            {
                SafeHouse.Config.SaveConfig();
            }
        }

        public void OnGUI()
        {
            if (!isOpen)
            {
                wasOpenLastPaint = false;
                TermWindow.StatusWanted = false;
                return;
            }

            // Sorting the list is expensive.  Only do it when the window is first re-opened, not on every single repaint:
            if (!wasOpenLastPaint)
                kOSProcessor.SortAllInstances();
            wasOpenLastPaint = true;

            if (uiGloballyHidden && kOS.Safe.Utilities.SafeHouse.Config.ObeyHideUI)
            {
                ClearPartHighlight(); // not drawn, so no hover can end it
                return;
            }

            // The terminals work out their state for the CPUs tab, also with their windows closed:
            TermWindow.StatusWanted = tab == PanelTab.Cpus;

            GUI.skin = HighLogic.Skin; // the text fields' cursor and selection, the pickers

            // Fixed width; as high as the content up to the room the launcher leaves, at the
            // corner the launcher is at (as upstream's auto-sized window ended up):
            ToolbarPanelLayout panel = Layout;
            GatherCpus();
            bool settings = tab == PanelTab.Settings;
            float height = panel.WindowHeight(ContentHeight(panel, settings), settings, rectToFit.height);
            windowRect = new Rect(rectToFit.xMax - panel.Width, anchorBottom ? rectToFit.yMax - height : rectToFit.y, panel.Width, height);
            windowRect = RectExtensions.ClampToRectAngle(windowRect, rectToFit);
            windowRect = GUI.Window(UNIQUE_ID, windowRect, DrawWindow, GUIContent.none, GUIStyle.none);
            KOSMouseBlocking.Show(ref mouseArea, "kOS toolbar panel", windowRect);
        }

        // The panel (Phase 4 step 4): two tabs in the terminal's flat style; layout and texts
        // in kOS.Safe.Screen (ToolbarPanelLayout, ToolbarCpuList, ToolbarSettings, TelnetPanelStatus).

        private enum PanelTab
        {
            Cpus,
            Settings
        }

        private static readonly string[] tabNames = { "CPUs", "Settings" };
        private PanelTab tab = PanelTab.Cpus;
        private bool anchorBottom; // the launcher is at the bottom: the panel grows upwards
        private ToolbarPanelLayout layout;
        private float contentScroll;
        private bool scrollDragging;
        private float scrollGrabOffset;
        private readonly ToolbarNumberField numberField = new ToolbarNumberField();
        private ToolbarSetting hoveredSetting; // in the current pass, for the description
        private readonly List<ToolbarCpuEntry> cpuEntries = new List<ToolbarCpuEntry>();
        private readonly List<kOSProcessor> cpuModules = new List<kOSProcessor>();

        private ToolbarPanelLayout Layout
        {
            get
            {
                float scale = TerminalChromeLayout.ClampScale(KOSStyle.UiScale);
                if (layout == null || layout.Scale != scale)
                    layout = new ToolbarPanelLayout(scale);
                return layout;
            }
        }

        private static Rect ToRect(ScreenRect rect)
        {
            return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        private static string Fit(string text, float width, GUIStyle style)
        {
            return StatusStrip.Shorten(text ?? string.Empty, width, measured => KOSStyle.Measure(style, measured), KOSStyle.Ellipsis);
        }

        /// <summary>The CPUs in kOS's order, each with what its row shows.</summary>
        private void GatherCpus()
        {
            cpuEntries.Clear();
            cpuModules.Clear();
            foreach (kOSProcessor module in kOSProcessor.AllInstances())
            {
                Part part = module == null ? null : module.part;
                if (part == null) continue;
                Vessel vessel = part.vessel;
                TermWindow window = module.GetWindow();
                TerminalStatus status = window == null ? null : window.Status;
                KOSNameTag tag = part.Modules.OfType<KOSNameTag>().FirstOrDefault();
                cpuEntries.Add(new ToolbarCpuEntry
                {
                    Vessel = vessel,
                    VesselName = vessel == null ? null : vessel.GetName(),
                    PartTitle = part.partInfo == null ? null : part.partInfo.title,
                    Tag = tag == null ? null : tag.nameTag,
                    HasStatus = status != null,
                    Kind = status == null ? StatusKind.Off : status.Kind,
                    StateText = status == null ? null : status.StateText,
                    WindowOpen = window != null && window.IsOpen,
                    TelnetClients = window == null ? 0 : window.NumTelnets()
                });
                cpuModules.Add(module);
            }
        }

        private float ContentHeight(ToolbarPanelLayout panel, bool settings)
        {
            return settings ? DrawSettings(panel, 0, 0, false, false) : ToolbarCpuList.Height(ToolbarCpuList.Rows(cpuEntries, panel), panel);
        }

        public void DrawWindow(int windowID)
        {
            ToolbarPanelLayout panel = Layout;
            bool settings = tab == PanelTab.Settings;
            var whole = new Rect(0, 0, windowRect.width, windowRect.height);

            // IMGUI keeps a text field's keyboard on a press elsewhere: a press anywhere in the
            // panel lets go of a number field, which then applies its typing (Q3); a press on a
            // field takes the keyboard again in this same pass.
            if (Event.current.type == EventType.MouseDown && GUI.GetNameOfFocusedControl().StartsWith(NUMBER_FIELD_PREFIX, StringComparison.Ordinal))
                GUIUtility.keyboardControl = 0;

            KOSStyle.Fill(whole, KOSStyle.PanelBackground);

            Rect header = ToRect(panel.Header());
            KOSStyle.Fill(header, KOSStyle.ChromeBackground);
            GUIStyle titleStyle = KOSStyle.TitleLabel(panel.FontSize);
            var title = new Rect(panel.Padding, 0, header.width - 2 * panel.Padding, header.height);
            KOSStyle.Text(title, Fit("kOS " + versionString, title.width, titleStyle), titleStyle, KOSStyle.ChromeText);
            for (int i = 0; i < tabNames.Length; ++i)
                if (KOSStyle.Tab(ToRect(panel.Tab(i, tabNames.Length)), tabNames[i], panel.FontSize, (int)tab == i, panel.Border))
                    SwitchTab((PanelTab)i);

            BeginHoverHousekeeping();
            hoveredSetting = null;
            DrawContent(panel, settings);
            if (settings)
                DrawDescription(panel);
            DrawFooter(panel);
            EndHoverHousekeeping();

            KOSStyle.Frame(whole, panel.Border, KOSStyle.FrameColor);
            GUI.SetNextControlName(""); // because if you don't then there is no such thing as the "non" control to move the focus to.
            // This is an invisible dummy control to "focus on" to, basically, unfocus, because Unity didn't
            // provide an unfocus method.
        }

        private void SwitchTab(PanelTab newTab)
        {
            ApplyPendingNumber();
            tab = newTab;
            contentScroll = 0;
            scrollDragging = false;
            GUI.FocusControl("");
        }

        /// <summary>The tab's rows in the content area, clipped and scrolled; the scroll bar when they do not fit.</summary>
        private void DrawContent(ToolbarPanelLayout panel, bool settings)
        {
            ScreenRect area = panel.Content(windowRect.height, settings);
            Rect view = ToRect(area);
            float contentHeight = ContentHeight(panel, settings);
            bool scrolls = contentHeight > view.height;
            contentScroll = ToolbarPanelLayout.ClampScroll(contentScroll, contentHeight, view.height);

            Event e = Event.current;
            if (e.type == EventType.ScrollWheel && scrolls && view.Contains(e.mousePosition))
            {
                contentScroll = ToolbarPanelLayout.ClampScroll(contentScroll + e.delta.y * panel.NoteHeight, contentHeight, view.height);
                e.Use();
            }

            bool mouseInView = view.Contains(e.mousePosition);
            float rowWidth = panel.RowWidth(scrolls);
            GUI.BeginGroup(view);
            try
            {
                // A press outside the view is not the rows' (one scrolled out of sight lies under
                // the tabs or the footer, which would lose the press to it):
                bool pressOutside = e.type == EventType.MouseDown && !mouseInView;
                if (settings && !pressOutside)
                    DrawSettings(panel, rowWidth, -contentScroll, true, mouseInView);
                else if (!pressOutside)
                    DrawCpus(panel, rowWidth, -contentScroll, view.height, mouseInView);
            }
            finally
            {
                GUI.EndGroup();
            }
            if (scrolls)
                DrawScrollBar(panel, area, contentHeight);
            else
                scrollDragging = false;
        }

        /// <summary>A thin bar at the content's right edge: drag the thumb; a press beside it moves a page.</summary>
        private void DrawScrollBar(ToolbarPanelLayout panel, ScreenRect area, float contentHeight)
        {
            ScreenRect track = panel.ScrollTrack(area);
            Rect trackRect = ToRect(track);
            Rect thumbRect = ToRect(panel.ScrollThumb(track, contentHeight, area.Height, contentScroll));
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && trackRect.Contains(e.mousePosition))
            {
                if (thumbRect.Contains(e.mousePosition))
                {
                    scrollDragging = true;
                    scrollGrabOffset = e.mousePosition.y - thumbRect.y;
                }
                else
                {
                    float page = e.mousePosition.y < thumbRect.y ? -area.Height : area.Height;
                    contentScroll = ToolbarPanelLayout.ClampScroll(contentScroll + page, contentHeight, area.Height);
                }
                e.Use();
            }
            else if (scrollDragging && !Input.GetMouseButton(0))
            {
                scrollDragging = false; // released, also outside the panel
            }
            else if (scrollDragging && e.type == EventType.MouseDrag)
            {
                contentScroll = panel.ScrollFor(e.mousePosition.y - scrollGrabOffset, track, contentHeight, area.Height);
                e.Use();
            }
            KOSStyle.Fill(trackRect, KOSStyle.ScrollTrack);
            KOSStyle.Fill(ToRect(panel.ScrollThumb(track, contentHeight, area.Height, contentScroll)), scrollDragging ? KOSStyle.ChromeText : KOSStyle.DimText);
        }

        // The CPUs tab:

        private void DrawCpus(ToolbarPanelLayout panel, float rowWidth, float top, float viewHeight, bool mouseInView)
        {
            List<ToolbarCpuRow> rows = ToolbarCpuList.Rows(cpuEntries, panel);
            if (rows.Count == 0)
            {
                KOSStyle.Text(new Rect(0, top, rowWidth, panel.SettingRowHeight), ToolbarCpuList.EMPTY_TEXT, KOSStyle.Label(panel.FontSize), KOSStyle.ChromeText);
                GUIStyle noteStyle = KOSStyle.Label(panel.SmallFontSize);
                string note = ToolbarCpuList.EmptyNote(HighLogic.LoadedSceneIsEditor);
                KOSStyle.Text(new Rect(0, top + panel.SettingRowHeight, rowWidth, panel.NoteHeight), Fit(note, rowWidth, noteStyle), noteStyle, KOSStyle.DimText);
                return;
            }

            string separator = KOSStyle.Separator;
            foreach (ToolbarCpuRow row in rows)
            {
                var rect = new Rect(0, top + row.Y, rowWidth, row.Height);
                if (rect.yMax <= 0 || rect.y >= viewHeight) continue; // scrolled out of view
                ToolbarCpuEntry entry = cpuEntries[row.Entry];
                if (row.Kind == ToolbarCpuRowKind.Vessel)
                {
                    GUIStyle vesselStyle = KOSStyle.TitleLabel(panel.FontSize);
                    KOSStyle.Text(rect, Fit(entry.VesselName, rect.width, vesselStyle), vesselStyle, KOSStyle.ChromeText);
                    KOSStyle.Fill(new Rect(rect.x, rect.yMax - panel.Border, rect.width, panel.Border), KOSStyle.FrameColor);
                    continue;
                }
                DrawCpuRow(panel, rect, entry, cpuModules[row.Entry], separator, mouseInView);
            }
        }

        /// <summary>A CPU: its name and tag, its state under it, the Open/Close button on the right; hovering highlights the part.</summary>
        private void DrawCpuRow(ToolbarPanelLayout panel, Rect rect, ToolbarCpuEntry entry, kOSProcessor module, string separator, bool mouseInView)
        {
            Event e = Event.current;
            if (mouseInView && rect.Contains(e.mousePosition))
            {
                KOSStyle.Fill(rect, KOSStyle.Hover);
                if (e.type == EventType.Repaint)
                    newHoverPart = module.part;
            }

            float lineHeight = Mathf.Floor(rect.height / 2f);
            var button = new Rect(rect.xMax - panel.ButtonWidth, Mathf.Round(rect.y + (rect.height - panel.SettingRowHeight) / 2f),
                panel.ButtonWidth, panel.SettingRowHeight);
            float left = rect.x + panel.Padding / 2f;
            float textWidth = Math.Max(0, button.x - panel.Padding - left);

            GUIStyle nameStyle = KOSStyle.Label(panel.FontSize);
            KOSStyle.Text(new Rect(left, rect.y, textWidth, lineHeight), Fit(ToolbarCpuList.CpuLabel(entry, separator), textWidth, nameStyle),
                nameStyle, KOSStyle.ChromeText);

            int dot = Math.Max(4, (int)Math.Round(panel.SmallFontSize * 0.6));
            float detailX = left;
            if (entry.HasStatus)
            {
                KOSStyle.DrawIcon(new Rect(left, rect.y + lineHeight, dot, lineHeight), KOSStyle.Icon.Dot, dot, KOSStyle.StatusColor(entry.Kind));
                detailX += dot + panel.Padding / 2f;
            }
            GUIStyle detailStyle = KOSStyle.Label(panel.SmallFontSize);
            float detailWidth = Math.Max(0, left + textWidth - detailX);
            KOSStyle.Text(new Rect(detailX, rect.y + lineHeight, detailWidth, lineHeight),
                Fit(ToolbarCpuList.Detail(entry, separator), detailWidth, detailStyle), detailStyle, KOSStyle.DimText);

            // The user's click takes the keyboard, as upstream's terminal button:
            if (KOSStyle.Button(button, entry.WindowOpen ? "Close" : "Open", panel.FontSize) && module.GetWindow() != null)
                module.ToggleWindow();
        }

        // The settings tab:

        /// <summary>
        /// The settings' sections from <paramref name="top"/>, drawn when <paramref name="draw"/>
        /// (else only measured); returns their height. Rows: a heading per section, a row per
        /// setting; the telnet restart button when a new port waits; Boot in the editor.
        /// </summary>
        private float DrawSettings(ToolbarPanelLayout panel, float rowWidth, float top, bool draw, bool mouseInView)
        {
            if (SafeHouse.Config == null) return 0;
            IList<ConfigKey> keys = SafeHouse.Config.GetConfigKeys();
            float y = top;
            foreach (ToolbarSettingSection section in ToolbarSettings.Sections(keys))
            {
                if (draw) DrawHeading(panel, new Rect(0, y, rowWidth, panel.HeadingHeight), section.Title);
                y += panel.HeadingHeight;
                foreach (ToolbarSetting setting in section.Settings)
                {
                    if (draw) DrawSetting(panel, new Rect(0, y, rowWidth, panel.SettingRowHeight), setting, mouseInView);
                    y += panel.SettingRowHeight;
                }
                if (section.Group == ToolbarSettingGroup.Telnet && TelnetRestartNeeded())
                {
                    if (draw) DrawTelnetRestart(panel, new Rect(0, y, rowWidth, panel.SettingRowHeight));
                    y += panel.SettingRowHeight;
                }
            }
            if (HighLogic.LoadedSceneIsEditor)
            {
                if (draw)
                {
                    DrawHeading(panel, new Rect(0, y, rowWidth, panel.HeadingHeight), "Boot");
                    DrawRereadBootButton(panel, new Rect(0, y + panel.HeadingHeight, rowWidth, panel.SettingRowHeight + panel.NoteHeight));
                }
                y += panel.HeadingHeight + panel.SettingRowHeight + panel.NoteHeight;
            }
            return y - top;
        }

        private static void DrawHeading(ToolbarPanelLayout panel, Rect where, string text)
        {
            GUIStyle style = KOSStyle.TitleLabel(panel.FontSize);
            KOSStyle.Text(where, text, style, KOSStyle.Accent);
        }

        private const string NUMBER_FIELD_PREFIX = "kOSConfig_";

        private static string NumberFieldName(ConfigKey key)
        {
            return NUMBER_FIELD_PREFIX + key.StringKey;
        }

        private void DrawSetting(ToolbarPanelLayout panel, Rect row, ToolbarSetting setting, bool mouseInView)
        {
            ConfigKey key = setting.Key;
            bool hovered = mouseInView && row.Contains(Event.current.mousePosition);
            if (hovered)
                hoveredSetting = setting;

            if (key.Value is bool)
            {
                bool value = (bool)key.Value;
                bool picked = KOSStyle.Toggle(row, value, setting.Label, panel.FontSize, panel.Border);
                if (picked != value)
                    key.Value = picked; // set only when changed
                return;
            }

            if (hovered)
                KOSStyle.Fill(row, KOSStyle.Hover);
            float labelWidth = Mathf.Round(row.width * 0.45f);
            GUIStyle labelStyle = KOSStyle.Label(panel.FontSize);
            var label = new Rect(row.x + 2, row.y, labelWidth - 2, row.height);
            KOSStyle.Text(label, Fit(setting.Label, label.width, labelStyle), labelStyle, KOSStyle.ChromeText);
            float inset = Math.Max(1, Mathf.Round(2 * panel.Scale));
            var control = new Rect(row.x + labelWidth, row.y + inset, row.width - labelWidth, row.height - 2 * inset);

            if (ToolbarSettings.Is(key, ToolbarSettings.FONT_NAME))
                DrawFontField(key, control, panel);
            else if (ToolbarSettings.Is(key, ToolbarSettings.TELNET_ADDRESS))
                DrawIPAddrField(key, control, panel);
            else if (key.Value is int)
                DrawNumberField(key, control, panel);
            else if (key.Value is double || key.Value is float)
                DrawSlider(key, control, panel);
            else
                KOSStyle.Text(control, Fit(Convert.ToString(key.Value, CultureInfo.InvariantCulture), control.width, labelStyle), labelStyle, KOSStyle.DimText);
        }

        /// <summary>A number: applied on Enter or when the field loses the keyboard (Q3); <c>Config</c> clamps it.</summary>
        private void DrawNumberField(ConfigKey key, Rect where, ToolbarPanelLayout panel)
        {
            string fieldName = NumberFieldName(key);
            bool hasFocus = GUI.GetNameOfFocusedControl() == fieldName;
            Event e = Event.current;
            bool enter = hasFocus && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            string shown = numberField.Shown(fieldName, (int)key.Value);
            // PANEL-02: as long as the key's range needs (a fixed 4 rejected five-digit telnet ports):
            string typed = KOSStyle.TextField(where, fieldName, shown, ConfigIntInput.MaxLength(key), panel.FontSize, panel.Border, hasFocus);
            int value;
            if (numberField.Edited(fieldName, typed, hasFocus, enter, out value))
            {
                key.Value = value;
                if (enter)
                    GUI.FocusControl(""); // the field lets go: the edit is applied
            }
        }

        /// <summary>The typing of a number field that is no longer drawn (tab changed, panel closed) is applied.</summary>
        private void ApplyPendingNumber()
        {
            string field;
            int value;
            if (!numberField.FinishPending(out field, out value) || SafeHouse.Config == null) return;
            foreach (ConfigKey key in SafeHouse.Config.GetConfigKeys())
                if (NumberFieldName(key) == field && key.Value is int)
                    key.Value = value;
        }

        /// <summary>A fraction (brightness): the slider and its value, rounded to hundredths (as <c>CONFIG:BRIGHTNESS</c>'s step).</summary>
        private static void DrawSlider(ConfigKey key, Rect where, ToolbarPanelLayout panel)
        {
            float value = Convert.ToSingle(key.Value);
            float min = Convert.ToSingle(key.MinValue), max = Convert.ToSingle(key.MaxValue);
            GUIStyle valueStyle = KOSStyle.Label(panel.FontSize);
            float valueWidth = Mathf.Round(4 * panel.FontSize * 0.7f);
            KOSStyle.Text(new Rect(where.xMax - valueWidth, where.y, valueWidth, where.height),
                (Math.Round(value, 2)).ToString("0.00", CultureInfo.InvariantCulture), valueStyle, KOSStyle.ChromeText);
            var slider = new Rect(where.x, where.y + Mathf.Round(where.height * 0.2f), where.width - valueWidth - panel.Padding, Mathf.Round(where.height * 0.6f));
            float picked = KOSStyle.Slider(slider, value, min, max);
            if (Math.Abs(picked - value) > 1e-6f)
            {
                double rounded = Math.Round(picked, 2);
                if (key.Value is double)
                    key.Value = rounded;
                else
                    key.Value = (float)rounded;
            }
        }

        private void DrawFontField(ConfigKey key, Rect where, ToolbarPanelLayout panel)
        {
            string name = Convert.ToString(key.Value, CultureInfo.InvariantCulture);
            string shown = TerminalFontChoice.IsPicked(name) ? name : "Default";
            bool clicked = KOSStyle.Button(where, Fit(shown, where.width - 2 * panel.Padding, KOSStyle.CenteredLabel(panel.FontSize)), panel.FontSize);
            if (clicked)
            {
                // Make a new picker if it's closed, or close it if it's already open.
                if (fontPicker == null)
                {
                    fontPicker = this.gameObject.AddComponent<ListPickerDialog>();
                    kOS.Screen.ListPickerDialog.ChangeAction onChange = delegate(String s)
                        {
                            // If the font is monospaced, we'll accept it, else we'll deny the attempt
                            // and not commit the change to the config fields:
                            bool ok = AssetManager.Instance.GetSystemFontByNameAndSize(s, 13, true) != null;
                            if (ok)
                                key.Value = s;
                            return ok;
                        };

                    kOS.Screen.ListPickerDialog.CloseAction onClose = delegate() { fontPicker = null; };

                    fontPicker.Summon(windowRect.x, windowRect.y + windowRect.height, 300,
                        key.Name, "(Only fonts detected as monospaced are shown.)",
                        key.Value.ToString(), AssetManager.Instance.GetSystemFontNames(), onChange, onClose
                        );
                }
                else
                {
                    fontPicker.Close();
                    Destroy(fontPicker);
                    fontPicker = null;
                }
            }
        }

        private void DrawIPAddrField(ConfigKey key, Rect where, ToolbarPanelLayout panel)
        {
            string address = Convert.ToString(key.Value, CultureInfo.InvariantCulture);
            bool clicked = KOSStyle.Button(where, Fit(address, where.width - 2 * panel.Padding, KOSStyle.CenteredLabel(panel.FontSize)), panel.FontSize);
            if (clicked)
            {
                // Make a new picker if it's closed, or close it if it's already open.
                if (ipAddrPicker == null)
                {
                    ipAddrPicker = this.gameObject.AddComponent<ListPickerDialog>();
                    kOS.Screen.ListPickerDialog.ChangeAction onChange = delegate(String s)
                    {
                        bool ok = TelnetMainServer.Instance.SetBindAddrFromString(s);
                        if (ok)
                            key.Value = s;
                        return ok;
                    };

                    kOS.Screen.ListPickerDialog.CloseAction onClose = delegate() { ipAddrPicker = null; };

                    ipAddrPicker.Summon(windowRect.x, windowRect.y + windowRect.height, 300,
                        "Telnet address (restart telnet to take effect)\n", null, 
                        "current: " + key.Value.ToString(), TelnetMainServer.GetAllAddresses(), onChange, onClose
                    );
                }
                else
                {
                    ipAddrPicker.Close();
                    ipAddrPicker = null;
                }
            }
        }

        /// <summary>The server reads its port only when it starts; a new address restarts it by itself.</summary>
        private static bool TelnetRestartNeeded()
        {
            TelnetMainServer server = TelnetMainServer.Instance;
            return server != null && SafeHouse.Config != null &&
                TelnetPanelStatus.RestartNeeded(server.IsListening, server.GetRunningPort(), SafeHouse.Config.TelnetPort);
        }

        /// <summary>
        /// Stops the server; as telnet stays enabled, it starts again at the next update with the
        /// new port, through the same permission checks as turning it on (a one-time permission
        /// is asked again). Clients are disconnected, as by a change of address.
        /// </summary>
        private static void DrawTelnetRestart(ToolbarPanelLayout panel, Rect where)
        {
            if (KOSStyle.Button(where, TelnetPanelStatus.RestartText(SafeHouse.Config.TelnetPort), panel.FontSize) && TelnetMainServer.Instance != null)
                TelnetMainServer.Instance.StopListening();
        }

        /// <summary>Under the settings: the hovered setting's description and script name, else what the settings are.</summary>
        private void DrawDescription(ToolbarPanelLayout panel)
        {
            Rect area = ToRect(panel.Description(windowRect.height));
            KOSStyle.Fill(new Rect(area.x, area.y, area.width, panel.Border), KOSStyle.FrameColor);
            GUIStyle style = KOSStyle.Label(panel.SmallFontSize);
            var first = new Rect(area.x, area.y + panel.Padding, area.width, panel.NoteHeight);
            var second = new Rect(area.x, first.yMax, area.width, panel.NoteHeight);
            if (hoveredSetting == null)
            {
                KOSStyle.Text(first, Fit(ToolbarSettings.GENERAL_NOTE, first.width, style), style, KOSStyle.DimText);
                return;
            }
            KOSStyle.Text(first, Fit(hoveredSetting.Key.Name, first.width, style), style, KOSStyle.ChromeText);
            KOSStyle.Text(second, Fit(ToolbarSettings.Detail(hoveredSetting, KOSStyle.Separator), second.width, style), style, KOSStyle.DimText);
        }

        // Both tabs:

        /// <summary>The footer: emergency suppress (in the warning color while on) and the telnet line.</summary>
        private void DrawFooter(ToolbarPanelLayout panel)
        {
            float height = windowRect.height;
            Rect footer = ToRect(panel.Footer(height));
            KOSStyle.Fill(footer, KOSStyle.ChromeBackground);
            KOSStyle.Fill(new Rect(footer.x, footer.y, footer.width, panel.Border), KOSStyle.FrameColor);

            ConfigKey suppress = SafeHouse.Config == null ? null : ToolbarSettings.FooterKey(SafeHouse.Config.GetConfigKeys());
            if (suppress != null && suppress.Value is bool)
                DrawSuppressAutopilotField(suppress, ToRect(panel.SuppressButton(height)), panel);

            GUIStyle style = KOSStyle.Label(panel.SmallFontSize);
            Rect line = ToRect(panel.TelnetLine(height));
            KOSStyle.Text(line, Fit(TelnetStatusMessage(), line.width, style), style, KOSStyle.DimText);
        }

        private void DrawSuppressAutopilotField(ConfigKey key, Rect where, ToolbarPanelLayout panel)
        {
            bool prevValue = (bool)key.Value;
            KOSStyle.Fill(where, KOSStyle.ChipBackground);
            if (where.Contains(Event.current.mousePosition))
                KOSStyle.Fill(where, KOSStyle.Hover);
            KOSStyle.Frame(where, panel.Border, prevValue ? KOSStyle.Warning : KOSStyle.FrameColor);
            GUIStyle style = KOSStyle.CenteredBoldLabel(panel.FontSize);
            string text = prevValue ? "Autopilot suppressed (click to resume)" : "Emergency suppress autopilot";
            KOSStyle.Text(where, Fit(text, where.width - 2 * panel.Padding, style), style, prevValue ? KOSStyle.Warning : KOSStyle.ChromeText);
            if (!GUI.Button(where, GUIContent.none, GUIStyle.none))
                return;
            key.Value = !prevValue;

            // When the button just got pressed in this pass (went from false to true):
            if (!prevValue)
            {
                Close();
                clickedOn = false;
            }
        }

        private string TelnetStatusMessage()
        {
            TelnetMainServer server = TelnetMainServer.Instance; // might not exist yet: we can't control the order in which monobehaviours are loaded
            if (server == null)
                return TelnetPanelStatus.Text(false, false, null, 0, 0, KOSStyle.Separator);
            bool isOn = server.IsListening;
            System.Net.IPAddress address = isOn ? server.GetRunningAddress() : null;
            return TelnetPanelStatus.Text(true, isOn, address == null ? null : address.ToString(), isOn ? server.GetRunningPort() : 0,
                server.ClientCount, KOSStyle.Separator);
        }

        private void DrawRereadBootButton(ToolbarPanelLayout panel, Rect where)
        {
            var button = new Rect(where.x, where.y, where.width, panel.SettingRowHeight);
            if (KOSStyle.Button(button, "Reread boot folder", panel.FontSize))
                kOSProcessor.SetBootListDirty();
            GUIStyle style = KOSStyle.Label(panel.SmallFontSize);
            var note = new Rect(where.x, button.yMax, where.width, panel.NoteHeight);
            KOSStyle.Text(note, Fit("For files added to the archive's boot folder in this editor.", note.width, style), style, KOSStyle.DimText);
        }

        /// <summary>Kept for mods that may call it; since Phase 4 step 4 the number fields keep their own typing.</summary>
        public void SetupBackingConfigInts()
        {
        }

        public void BeginHoverHousekeeping()
        {
            // OnGUI() gets called many times in different modes for different reasons.
            // This logic only works right when used during the Repaint pass of OnGUI().
            if (Event.current.type != EventType.Repaint)
                return;

            // Track whether or not the mouse is over the desired GUI element on *this* OnGUI
            // by clearing out what it was before first:
            newHoverPart = null;
        }

        /// <summary>
        /// Control the highlighting of parts in the vessel depending on whether or not
        /// the mouse was hovering in the right spot to cause a highlight.
        /// </summary>
        public void EndHoverHousekeeping()
        {
            // OnGUI() gets called many times in different modes for different reasons.
            // This logic only works right when used during the Repaint pass of OnGUI().
            if (Event.current.type != EventType.Repaint)
                return;

            // If we were already highlighting a part, and are no longer hovering over
            // that part area in the panel, then de-highlight it:
            if (prevHoverPart != null && prevHoverPart != newHoverPart)
            {
                prevHoverPart.SetHighlightColor(originalPartHighlightColor);
                prevHoverPart.SetHighlight(false, false);
            }

            // If we are now hovering over a part area in the panel, then start highlighting it,
            // remembering what it was before so it cab be set back again.
            if (newHoverPart != null && prevHoverPart != newHoverPart)
            {
                originalPartHighlightColor = newHoverPart.highlightColor;
                newHoverPart.SetHighlightColor(ourPartHighlightColor);
                newHoverPart.SetHighlight(true, false);
            }

            prevHoverPart = newHoverPart;
        }

        /// <summary>
        /// The part highlighted for a hovered row goes back to normal when the panel is not drawn
        /// (closed, or hidden with the UI): upstream left it highlighted.
        /// </summary>
        private void ClearPartHighlight()
        {
            if (prevHoverPart != null)
            {
                prevHoverPart.SetHighlightColor(originalPartHighlightColor);
                prevHoverPart.SetHighlight(false, false);
            }
            prevHoverPart = null;
            newHoverPart = null;
        }

        /// <summary>Whatever the most recent GUILayout element was, remember the part it was for
        /// if the mouse was hovering in it. (Unused since Phase 4 step 4, which draws without
        /// GUILayout; kept as it is public.)</summary>
        /// <param name="part">The part that just had info drawn for it</param>
        public void CheckHoverOnPreviousGUIElement(Part part)
        {
            // OnGUI() gets called many times in different modes for different reasons.
            // This logic only works right when used during the Repaint pass of OnGUI().
            if (Event.current.type != EventType.Repaint)
                return;

            if (GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                newHoverPart = part;
        }
    }
}
