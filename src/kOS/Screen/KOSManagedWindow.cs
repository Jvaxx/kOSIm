using System.Collections.Generic;
using kOS.Safe.UserIO;
using UnityEngine;

namespace kOS.Screen
{
    /// <summary>
    /// kOSManagedWindow is for any Unity Monobehavior that you'd like to
    /// have contain a GUI.Window, and you need kOS to keep track of the
    /// window stacking for which click is on top of which other click.
    /// Unity's built in systems for this don't work well at all, so
    /// we had to make up our own.
    /// </summary>
    public abstract class KOSManagedWindow : MonoBehaviour
    {
        // The static values are for the way the windows keep track of each other:

        // Give each instance of TermWindow a unique ID block to ensure it can create
        // Unity windows that don't clash:
        private static int termWindowIDRange = 215300; // I literally just mashed the keyboard to get a unique number.
        private static int windowsMadeSoFar;
        // Keep track of the stacking order of the Terminal windows relative to each other.
        // surprisingly, the Unity GUi doesn't do this automatically itself:
        private static List<KOSManagedWindow> depthSort = new List<KOSManagedWindow>();

        private static int dragTolerance = 2; // mouse pixel movement of this or less counts as a click and not a drag.


        private Rect windowRect;

        private int uniqueId; // For the window attached to this widget.

        /// subclasses of KOSManagedWindow can use these to see the mouse position
        /// by full screen coords, or relative to their windowRect:
        private Vector2 mousePosAbsolute;

        private Vector2 mousePosRelative;

        /// When doing a mousedown, and mouseup to form a click, this is the
        /// position of the most recent mousedown:
        private Vector2 mouseButtonDownPosAbsolute;

        private Vector2 mouseButtonDownPosRelative;

        private bool isOpen;

        private bool isPowered;

        private readonly string lockIdName;

        // Keyboard focus and the control lock are decided by the shared policy (INPUT-01):
        private InputOwnership.Window inputWindow;

        // The mouse over the drawn window is kept from the scene (INPUT-01 mouse half), opted out or not:
        private MouseBlocking.Area mouseArea;

        private bool optOutOfControlLocking;
        /// <summary>
        /// Fixes #2568 - If the window is one where Unity can handle doing the keyboard focus
        /// properly itself, like a Unity IMGUI window, then it should set this to true so it
        /// will avoid using KSP's more high level control locking scheme whcih is a bit flaky at times:
        /// </summary>
        public bool OptOutOfControlLocking
        {
            get { return optOutOfControlLocking; }
            set
            {
                if (value == optOutOfControlLocking)
                    return;
                optOutOfControlLocking = value;
                if (inputWindow == null)
                    return;
                // Register again with the new lock choice, keeping the open state but not the focus:
                KOSInputOwnership.Instance.Unregister(inputWindow);
                inputWindow = null;
                if (isOpen)
                    KOSInputOwnership.Instance.Open(InputWindow, false);
                if (!isPowered)
                    KOSInputOwnership.Instance.SetPowered(InputWindow, false);
            }
        }

        protected KOSManagedWindow(string lockIdName = "")
        {
            // multiply by 50 so there's a range for future expansion for other GUI objects inside the window:
            uniqueId = termWindowIDRange + (windowsMadeSoFar * 50);
            ++windowsMadeSoFar;
            // When the lockIdName is not given, then manufacture a unique one:
            if (lockIdName.Length == 0)
                this.lockIdName = "KOSManagedWindow ID:" + uniqueId;
            else
                this.lockIdName = lockIdName;
            isPowered = true;
        }

        /// <summary>This window's entry in the shared keyboard-ownership policy, registered on first use.</summary>
        protected InputOwnership.Window InputWindow
        {
            get
            {
                if (inputWindow == null)
                    inputWindow = KOSInputOwnership.Instance.Register(lockIdName, !optOutOfControlLocking);
                return inputWindow;
            }
        }

        /// <summary>True when keys typed now are meant for this window.</summary>
        public bool HasKeyboard
        {
            get { return inputWindow != null && KOSInputOwnership.Instance.HasKeyboard(inputWindow); }
        }

        /// <summary>An unpowered window loses keyboard focus for good and cannot take it.</summary>
        public bool IsPowered
        {
            get { return isPowered; }
            set
            {
                isPowered = value;
                KOSInputOwnership.Instance.SetPowered(InputWindow, value);
            }
        }

        protected static int TermWindowIDRange
        {
            get { return termWindowIDRange; }
            set { termWindowIDRange = value; }
        }

        protected static int WindowsMadeSoFar
        {
            get { return windowsMadeSoFar; }
            set { windowsMadeSoFar = value; }
        }

        protected static List<KOSManagedWindow> DepthSort
        {
            get { return depthSort; }
            set { depthSort = value; }
        }

        protected static int DragTolerance
        {
            get { return dragTolerance; }
            set { dragTolerance = value; }
        }

        protected Rect WindowRect
        {
            get { return windowRect; }
            set { windowRect = value; }
        }

        protected int UniqueId
        {
            get { return uniqueId; }
            set { uniqueId = value; }
        }

        /// <summary>
        /// subclasses of KOSManagedWindow can use this to see the mouse position
        /// by full screen coords
        /// </summary>
        protected Vector2 MousePosAbsolute
        {
            get { return mousePosAbsolute; }
            set { mousePosAbsolute = value; }
        }

        /// <summary>
        /// subclasses of KOSManagedWindow can use this to see the mouse position
        /// relative to their windowRect
        /// </summary>
        protected Vector2 MousePosRelative
        {
            get { return mousePosRelative; }
            set { mousePosRelative = value; }
        }

        /// <summary>
        /// When doing a mousedown, and mouseup to form a click, this is the
        /// absolute position of the most recent mousedown:
        /// </summary>
        protected Vector2 MouseButtonDownPosAbsolute
        {
            get { return mouseButtonDownPosAbsolute; }
            set { mouseButtonDownPosAbsolute = value; }
        }

        /// <summary>
        /// When doing a mousedown, and mouseup to form a click, this is the
        /// relative position of the most recent mousedown:
        /// </summary>
        protected Vector2 MouseButtonDownPosRelative
        {
            get { return mouseButtonDownPosRelative; }
            set { mouseButtonDownPosRelative = value; }
        }

        protected void OnDestroy()
        {
            depthSort.Remove(this);
            KOSMouseBlocking.Unregister(ref mouseArea);
            if (inputWindow != null)
            {
                InputOwnership.Window window = inputWindow;
                inputWindow = null;
                KOSInputOwnership.Instance.Unregister(window);
            }
        }

        /// <summary>
        /// Call from OnGUI right after drawing the window, and only when it was drawn: the mouse
        /// over <paramref name="drawnRect"/> stays out of the scene while the window keeps drawing.
        /// </summary>
        protected void BlockMouseOver(Rect drawnRect)
        {
            KOSMouseBlocking.Show(ref mouseArea, lockIdName, drawnRect);
        }

        /// <summary>
        /// Give this window the keyboard focus, as a user's click on it does. The shared policy
        /// takes it from any other kOS window and sets the control lock if this window uses one.
        /// </summary>
        public virtual void GetFocus()
        {
            KOSInputOwnership.Instance.Click(InputWindow);
        }

        /// <summary>
        /// Take the keyboard focus away from this window, as a click outside it does.
        /// Does nothing when another window has the focus.
        /// </summary>
        public virtual void LoseFocus()
        {
            if (inputWindow != null && inputWindow.WantsFocus)
                KOSInputOwnership.Instance.Click(null);
        }

        /// <summary>Open the window as a user's action does: it takes the keyboard focus.</summary>
        public void Open()
        {
            Open(true);
        }

        /// <summary>
        /// Implement this to make the window appear when it wasn't there before.
        /// be sure to also call the base.Open() as well, because it has
        /// important logic in it.
        /// </summary>
        /// <param name="takeFocus">True when the user opened it; false for an action group or a
        /// script, which must not take the keyboard and flight controls.</param>
        public virtual void Open(bool takeFocus)
        {
            isOpen = true;
            BringToFront();
            KOSInputOwnership.Instance.Open(InputWindow, takeFocus);
            
            // I was having trouble before with this.  If it crops up again, I want
            // to re-enable this line:
            // DumpDepthSort();
        }
        
        /// <summary>
        /// Implement this to make the window disappear when it was there before:
        /// be sure to also call the base.Close() as well, because it has
        /// important logic in it.
        /// </summary>
        public virtual void Close()
        {
            isOpen = false;
            depthSort.Remove(this);
            KOSInputOwnership.Instance.Close(InputWindow);

            // I was having trouble before with this.  If it crops up again, I want
            // to re-enable this line:
            // DumpDepthSort();
        }
        
        /// <summary>
        /// Setting it bypasses the subclass's Open()/Close() logic, but keeps the keyboard
        /// focus consistent: hiding releases it and showing never takes it.
        /// </summary>
        public bool IsOpen
        {
            get { return isOpen; }
            protected set
            {
                if (value == isOpen)
                    return;
                isOpen = value;
                if (value)
                    KOSInputOwnership.Instance.Open(InputWindow, false);
                else
                    KOSInputOwnership.Instance.Close(InputWindow);
            }
        }
        
        public void DumpDepthSort() // exists purely for debugging.
        {
            for (int i=0 ; i< depthSort.Count ; ++i)
            {
                KOSManagedWindow w = depthSort[i];
            }
        }
        
        /// <summary>
        /// Pass in the absolute GUI screen location of a mouse click to decide whether
        /// or not this widget gets keyboard focus because of that click.
        /// (Clicking outside the window takes focus away.  Clicking inside
        /// the window gives focus to the window and brings it to the front.)
        /// </summary>
        /// <param name="absMousePos">Absolute position of mouse on whole screen</param>
        /// <returns>True if the window got focused, false if it didn't.</returns>
        public bool FocusClickLocationCheck(Vector2 absMousePos)
        {
            bool wasInside = false;
            if (IsInsideMyExposedPortion(absMousePos))
            {
                BringToFront();
                GetFocus();
                wasInside = true;
            }
            else
            {
                // Every open window checks the same click: only the one that had the focus
                // gives it up, so the order of their Update() calls does not matter.
                LoseFocus();
            }
            return wasInside;
        }

        /// <summary>
        /// Cause this window to become front most. The keyboard focus is separate: see
        /// <see cref="GetFocus"/>.
        /// </summary>
        public virtual void BringToFront()
        {
            // Remove me from where I was in the depth list, and put me at the front instead:
            if (depthSort.Exists(t => t == this))
            {
                depthSort.Remove(this);
            }
            depthSort.Insert(0,this);
            GUI.BringWindowToFront(uniqueId);
        }

        // Returns true only if the position is inside this window and NOT inside
        // any terminal windows that are closer to the front.
        // (This is the sort of basic checking that Unity's GUI *should* do itself
        // but it seems quite bad at it.)
        public bool IsInsideMyExposedPortion(Vector2 posAbsolute)
        {
            if (windowRect.Contains(posAbsolute))
            {
                int myDepthIndex = depthSort.FindIndex(t => t == this);
                bool insideHigher = false;
                for (int stackedIndex = myDepthIndex - 1 ; (!insideHigher) && stackedIndex >= 0 ; --stackedIndex)
                {
                    insideHigher = depthSort[stackedIndex].windowRect.Contains(posAbsolute);
                }
                if (!insideHigher)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// When you subclass KOSManagedWindow, make sure that you call this
        /// from inside your Update.  It does not use OnGUI because of the fact
        /// that the OnGUI event handler is broken - it only sends MouseDown 
        /// and MouseUp events when the mouse is OUTSIDE the window, which is
        /// utterly backward, and it's hard to work out how to fix this,
        /// given how badly documented the Unity GUI API is.  If anyone who
        /// actually understands the Unity GUI system wants to fix this,
        /// please feel free to do so.
        /// </summary>
        /// <returns>True if there was a mouseclick within this window.</returns>
        public bool UpdateLogic()
        {
            KOSInputOwnership.Update();
            if (!IsOpen) return false;

            // Input.mousePosition, unlike Event.current.MousePosition, puts the origin at the
            // lower-left instead of upper-left of the screen, thus the subtraction in the y coord below:
            mousePosAbsolute = new Vector2( Input.mousePosition.x, UnityEngine.Screen.height - Input.mousePosition.y);
    
            // Mouse coord within the window, rather than within the screen.
            mousePosRelative = new Vector2( mousePosAbsolute.x - windowRect.xMin, mousePosAbsolute.y - windowRect.yMin);
    
            bool clickUp = false;
            if (Input.GetMouseButtonDown(0))
            {
                mouseButtonDownPosAbsolute = mousePosAbsolute;
                mouseButtonDownPosRelative = mousePosRelative;
            }
    
            if (Input.GetMouseButtonUp(0))
            {
                clickUp = true;
                if (Vector2.Distance(mousePosAbsolute,mouseButtonDownPosAbsolute) <= dragTolerance)
                {
                    FocusClickLocationCheck(mousePosAbsolute);
                }
            }
            return IsInsideMyExposedPortion(mousePosAbsolute) && clickUp;
        }
    }
}
