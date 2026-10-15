using System;
using System.Collections.Generic;
using System.IO;
using kOS.Safe.Exceptions;
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
    /// </summary>
    /// This was MEANT to be a nested class inside KOSTextEditPopup, but Unity can't
    /// handle a MonoBehaviour class nested inside a MonoBehaviour class.  It wants them
    /// all flat and globally accessible
    public class DelegateDialog : MonoBehaviour
    {
        private KOSTextEditPopup   parent;
        private string             message = "";
        private List<string>       options;
        private List<DialogAction> actions;
        private bool               invoked;
        private string             error = "";
        private MouseBlocking.Area mouseArea;

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
            this.parent = parent;
            this.parent.Freeze(true);
            this.message = message;
            this.options = options;
            this.actions = actions;
            error = "";
            invoked = true;
        }
        
        public void OnGUI()
        {
            if (invoked && parent.IsOpen)
            {
                float guessWidth = GUI.skin.label.CalcSize( new GUIContent(message) ).x;
                Rect drawn = GUILayout.Window( parent.GetUniqueId()+1, new Rect( parent.GetRect().xMin+200,
                                                 parent.GetRect().yMin+10,
                                                 guessWidth,
                                                 0) , DrawConfirm, "Confirm", GUILayout.ExpandWidth(true) );
                KOSMouseBlocking.Show(ref mouseArea, "DelegateDialog", drawn);
            }
        }

        public void OnDestroy()
        {
            KOSMouseBlocking.Unregister(ref mouseArea);
        }
        
        public void DrawConfirm( int windowID )
        {
            if (invoked)
            {
                GUILayout.Label( message );
                if (error.Length > 0) GUILayout.Label(error);
                for (int bNum = 0 ; bNum < options.Count ; bNum++)
                {
                    if (GUILayout.Button( options[bNum]))
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
                        break;
                    }
                }
            }
        }
    }
}