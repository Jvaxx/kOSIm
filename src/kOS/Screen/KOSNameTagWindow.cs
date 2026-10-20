using kOS.Utilities;
using UnityEngine;
using kOS.Module;
using kOS.Safe.UserIO;
using System;
using System.Collections.Generic;
using kOS.Safe.Screen;

namespace kOS.Screen
{    
    public class KOSNameTagWindow : MonoBehaviour
    {
        private KOSNameTag attachedModule;
        private Rect windowRect;
        private string tagValue;
        // ReSharper disable RedundantDefaultFieldInitializer
        private bool wasFocusedOnce = false; // "explicit", not "redundant".
        private int numberOfRepaints = 0; // "explicit", not "redundant".
        private bool gameEventHooksExist = false; // "explicit", not "redundant".
        private int myWindowId; // must be unique for Unity to not mash two nametag windows togehter.
        // In flight, typing a tag takes the terminal's control lock until the window closes (INPUT-01).
        // The VAB/SPH keeps its own EditorLogic lock below instead.
        private InputOwnership.Window inputWindow;
        // In every scene the mouse over the window stays out of the scene, and for two frames
        // after Accept or Cancel closes it (#2759):
        private MouseBlocking.Area mouseArea;
        private FlatWindowLayout layout;
        
        // ReSharper enable RedundantDefaultFieldInitializer

        public void Invoke(KOSNameTag module, string oldValue)
        {
            attachedModule = module;
            tagValue = oldValue;
            myWindowId = GetInstanceID(); // Use the Id of this MonoBehaviour to guarantee unique window ID.
            
            Vector3 screenPos = GetViewportPosFor(attachedModule.part.transform.position);

            // screenPos is in coords from 0 to 1, 0 to 1, not screen pixel coords.
            // Transform it to pixel coords:
            float xPixelPoint = screenPos.x * UnityEngine.Screen.width;
            float yPixelPoint = (1-screenPos.y) * UnityEngine.Screen.height;
            // At the part; sized and kept on screen in OnGUI (Phase 4 step 5):
            windowRect = new Rect(xPixelPoint, yPixelPoint, 0, 0);

            // Please don't delete these.  They're not being used, but that's because we haven't
            // finished prettying up the interface with the tag line and so the coords aren't
            // being made use of yet.  But keep this in the code so I can remember how I did the math:
            // --------------------------------------------------------------------------------------
            // bool drawOnLeft = (screenPos.x > 0.5f);
            // float xPixelWindow = (drawOnLeft ? screenPos.x - 0.3f : screenPos.x + 0.2f) * UnityEngine.Screen.width;
            // float tagLineWidth = (drawOnLeft) ? (xPixelPoint - xPixelWindow) : (xPixelWindow - xPixelPoint - windowWidth);
            // tagLineRect = new Rect(xPixelPoint, yPixelPoint, tagLineWidth, 3);
            // SafeHouse.Logger.Log("tagLineRect = " + tagLineRect );

            SetEnabled(true);

            if (inputWindow == null)
                inputWindow = KOSInputOwnership.Instance.Register("KOSNameTag", !HighLogic.LoadedSceneIsEditor);
            KOSInputOwnership.Instance.Open(inputWindow, !KOSInputOwnership.IsScriptedInvocation);

            if (HighLogic.LoadedSceneIsEditor)
                attachedModule.part.SetHighlight(false, false);
            
        }
        
        /// <summary>
        /// Catch the event of the part disappearing, from crashing or
        /// from unloading from distance or scene change, and ensure
        /// the window closes if it was open when that happens:
        /// </summary>
        /// <param name="whichPartWentAway">The callback is called for EVERY part
        /// that ever goes away, so we have to check if it's the right one</param>
        public void GoAwayEventCallback(Part whichPartWentAway)
        {
            if (whichPartWentAway != attachedModule.part)
                return;
            
            Close();
        }

        /// <summary>
        /// If you try to set a Unity.Behaviour.enabled to false when it already IS false,
        /// and Unity hasn't fully finished configuring the MonoBehaviour yet, the Property's
        /// "set" code throws a null ref error. How lame is that?
        /// That's why I wrapped every attempt to set enabled's value with this check, because KSP
        /// tries running my hooks in this class before Unity's ready for them.
        /// </summary>
        private void SetEnabled(bool newVal)
        {
            // ReSharper disable once RedundantCheckBeforeAssignment
            if (newVal != enabled)
                enabled = newVal;

            if (enabled)
            {
                if (! gameEventHooksExist)
                {
                    GameEvents.onPartDestroyed.Add(GoAwayEventCallback);
                    GameEvents.onPartDie.Add(GoAwayEventCallback);
                    gameEventHooksExist = true;
                }
            }
            else
            {
                if (gameEventHooksExist)
                {
                    GameEvents.onPartDestroyed.Remove(GoAwayEventCallback);
                    GameEvents.onPartDie.Remove(GoAwayEventCallback);                
                    gameEventHooksExist = false;                    
                }
            }
        }

        /// <summary>
        /// Get the position in screen coordinates that the 3d world coordinates
        /// project onto, abstracting the two different ways KSP has to access
        /// the camera depending on view mode.
        /// Returned coords are in a system where the screen viewport goes from
        /// (0,0) to (1,1) and the Z coord is how far from the screen it is
        /// (-Z means behind you).
        /// </summary>
        private Vector3 GetViewportPosFor( Vector3 v )
        {
            return Utils.GetCurrentCamera().WorldToViewportPoint(v);
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

        /// <summary>The part's title, as the toolbar panel names it (its internal name when it has none).</summary>
        private string PartTitle
        {
            get
            {
                Part part = attachedModule == null ? null : attachedModule.part;
                if (part == null) return string.Empty;
                return part.partInfo != null && !string.IsNullOrEmpty(part.partInfo.title) ? part.partInfo.title : part.name;
            }
        }

        public void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                ++numberOfRepaints;
            
            if (! enabled)
                return;
            if (HighLogic.LoadedSceneIsEditor)
                EditorLogic.fetch.Lock(false, false, false, "KOSNameTagLock");

            GUI.skin = HighLogic.Skin; // the text field's caret and selection

            // Phase 4 step 5: a flat window at the part, kept on screen.
            FlatWindowLayout flat = Layout;
            float height = flat.WindowHeight(flat.RowHeight + flat.Gap + flat.RowHeight, FlatWindowBottom.Buttons);
            ScreenRect placed = WindowPlacement.KeepOnScreen(new ScreenRect(windowRect.x, windowRect.y, flat.NameTagWidth, height),
                UnityEngine.Screen.width, UnityEngine.Screen.height);
            windowRect = new Rect(placed.X, placed.Y, placed.Width, placed.Height);
            windowRect = GUI.Window(myWindowId, windowRect, DrawWindow, GUIContent.none, GUIStyle.none);
            KOSMouseBlocking.Show(ref mouseArea, "KOSNameTag", windowRect);
            
            // Ensure that the first time the window is made, it gets keybaord focus,
            // but allow the focus to leave the window after that:
            // The reason for the "number of repaints" check is that OnGUI has to run
            // through several initial passes before all the components are present,
            // and if you call FocusControl on the first few passes, it has no effect.
            if (numberOfRepaints >= 2 && ! wasFocusedOnce)
            {
                GUI.FocusWindow(myWindowId);
                GUI.FocusControl("NameTagField");
                wasFocusedOnce = true;
            }
        }

        public void DrawWindow( int windowID )
        {
            if (! enabled)
                return;
            
            Event e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return ||
                    e.keyCode == KeyCode.KeypadEnter)
                {
                    e.Use();
                    attachedModule.TypingDone(tagValue);
                    Close();
                    return;
                }
                if (e.keyCode == KeyCode.Escape) // step 5 (Q4): Esc cancels
                {
                    e.Use();
                    Close();
                    return;
                }
            }

            FlatWindowLayout flat = Layout;
            float width = windowRect.width, height = windowRect.height;
            ScreenRect close = flat.CloseButton(width);
            KOSStyle.FlatWindow(flat, width, height, "Name tag", close.X);
            bool cancelled = KOSStyle.IconButton(ToRect(close), KOSStyle.Icon.Close, Math.Max(6, (int)Math.Round(flat.HeaderHeight * 0.6)));

            ScreenRect body = flat.Body(width, height, FlatWindowBottom.Buttons);
            GUIStyle label = KOSStyle.Label(flat.FontSize);
            var titleRect = new Rect(body.X, body.Y, body.Width, flat.RowHeight);
            KOSStyle.Text(titleRect, StatusStrip.Shorten(PartTitle, titleRect.width, text => KOSStyle.Measure(label, text), KOSStyle.Ellipsis),
                label, KOSStyle.DimText);
            var field = new Rect(body.X, body.Y + flat.RowHeight + flat.Gap, body.Width, flat.RowHeight);
            tagValue = KOSStyle.TextField(field, "NameTagField", tagValue, int.MaxValue, flat.FontSize, flat.Border,
                GUI.GetNameOfFocusedControl() == "NameTagField");

            GUIStyle measure = KOSStyle.CenteredBoldLabel(flat.FontSize);
            List<ScreenRect> buttons = flat.Buttons(width, height, new List<float>
            {
                flat.ButtonWidth(KOSStyle.Measure(measure, "Cancel")),
                flat.ButtonWidth(KOSStyle.Measure(measure, "Accept"))
            });
            cancelled |= KOSStyle.Button(ToRect(buttons[0]), "Cancel", flat.FontSize);
            bool accepted = KOSStyle.Button(ToRect(buttons[1]), "Accept", flat.FontSize, true);
            KOSStyle.FlatWindowFrame(flat, width, height, true);

            if (accepted)
            {
                e.Use();
                attachedModule.TypingDone(tagValue);
                Close();
            }
            else if (cancelled)
            {
                e.Use();
                Close();
            }
            else
                GUI.DragWindow(ToRect(flat.Header(width)));

            // Before going any further, suppress any remaining unprocessed clicks
            // so they don't end up causing the editor to detach parts:
            if (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.MouseDrag)
                e.Use();
        }

        private static Rect ToRect(ScreenRect rect)
        {
            return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        }
        
        public void Close()
        {
            if (HighLogic.LoadedSceneIsEditor)
                EditorLogic.fetch.Unlock("KOSNameTagLock");
            
            SetEnabled(false);

            if (inputWindow != null)
                KOSInputOwnership.Instance.Close(inputWindow);

            if (HighLogic.LoadedSceneIsEditor)
                attachedModule.part.SetHighlight(false, false);
        }
        
        public void OnDestroy()
        {
            SetEnabled(false);
            KOSMouseBlocking.Unregister(ref mouseArea);
            if (inputWindow != null)
            {
                InputOwnership.Window window = inputWindow;
                inputWindow = null;
                KOSInputOwnership.Instance.Unregister(window);
            }
        }
    }
}