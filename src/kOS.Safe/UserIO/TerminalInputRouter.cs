using System;
using kOS.Safe.Screen;

namespace kOS.Safe.UserIO
{
    /// <summary>
    /// What a <see cref="TerminalInputRouter{TClient}"/> needs from the terminal window it
    /// routes for (the game's <c>kOS.Screen.TermWindow</c>, or a test fixture).
    /// Main thread only.
    /// </summary>
    /// <typeparam name="TClient">the window's telnet client type</typeparam>
    public interface ITerminalInputHost<TClient> where TClient : class
    {
        /// <summary>The processor's screen; null while the window is not attached.</summary>
        IScreenBuffer Screen { get; }

        /// <summary>The processor's interpreter (the same object as the screen); may be null.</summary>
        IInterpreter Interpreter { get; }

        /// <summary>False until the processor's first update has set up the CPU (#2925).</summary>
        bool HasBooted { get; }

        /// <summary>Whether a client still sends to this window; it may detach while its keys are read.</summary>
        bool IsClientAttached(TClient client);

        bool TryReadClientChar(TClient client, bool includeText, out char ch);

        /// <summary>Ctrl-D or Ctrl-X at the start of a command from the GUI (no client).</summary>
        void CloseWindow();

        /// <summary>Ctrl-D or Ctrl-X at the start of a command from a telnet client.</summary>
        void DetachClient(TClient client);

        /// <summary>The client asked for a full redraw.</summary>
        void RepaintClient(TClient client);

        /// <summary>A key was typed or queued: play the key click if the window wants one.</summary>
        void KeyClick();
    }

    /// <summary>
    /// The terminal window's input routing (Phase 2, P2-1; moved unchanged out of
    /// <c>TermWindow</c>): every key from the GUI, a telnet client or a mod goes through
    /// <see cref="ProcessOneInputChar"/>, which types it at the prompt, queues it as
    /// type-ahead (also read by a running script's <c>GETCHAR</c>), or acts on it (BREAK,
    /// detach, repaint, resize). Each update the window calls <see cref="ProcessClientInput"/>
    /// and then <see cref="ProcessUnconsumedInput"/>. Main thread only.
    /// </summary>
    /// <typeparam name="TClient">the window's telnet client type</typeparam>
    public sealed class TerminalInputRouter<TClient> where TClient : class
    {
        private readonly ITerminalInputHost<TClient> host;
        private ExpectNextChar inputExpected = ExpectNextChar.NORMAL;
        private int pendingWidth; // width to come from a resize combo.
        private int nextClientToDrain; // clients take turns at going first in ProcessClientInput()

        // The game's limits; tests change them to measure their effect.
        public int ClientCharsPerUpdate = TerminalInputPump.TELNET_CHARS_PER_UPDATE;
        public int TypeAheadLimit = TerminalInputPump.TYPE_AHEAD_LIMIT;
        public int PromptCharsPerUpdate = TerminalInputPump.PROMPT_CHARS_PER_UPDATE;
        public TimeSpan TimePerUpdate = TerminalInputPump.TimePerUpdate;

        public TerminalInputRouter(ITerminalInputHost<TClient> host)
        {
            if (host == null) throw new ArgumentNullException("host");
            this.host = host;
        }

        /// <summary>
        /// Read the attached clients' keys into the terminal, within the per-update budgets.
        /// </summary>
        /// <param name="clients">the clients attached at the start of this update</param>
        /// <returns>how many keys were processed</returns>
        public int ProcessClientInput(TClient[] clients)
        {
            if (clients.Length == 0)
                return 0; // nearly always: no key sources, budgets or closures to make (P-02)
            return DrainClients(clients);
        }

        // Apart from ProcessClientInput: a method's captured variables are allocated on
        // entering it, before any early return.
        private int DrainClients(TClient[] clients)
        {
            IScreenBuffer screen = host.Screen;
            var sources = new TerminalInputPump.KeySource[clients.Length];
            for (int i = 0; i < clients.Length; ++i)
            {
                TClient client = clients[i];
                sources[i] = (bool includeText, out char ch) =>
                {
                    // Stop reading from a client that got detached part way through:
                    if (!host.IsClientAttached(client))
                    {
                        ch = '\0';
                        return false;
                    }
                    return host.TryReadClientChar(client, includeText, out ch);
                };
            }
            return TerminalInputPump.DrainTelnets(sources, ref nextClientToDrain,
                () => screen.CharInputQueue.Count, TypeAheadLimit,
                () => new InputBudget(ClientCharsPerUpdate, TimePerUpdate),
                (index, ch) => ProcessOneInputChar(ch, clients[index], true, true));
        }

        /// <summary>
        /// When the input queue is not empty and the program or command is done such that
        /// the input cursor is now all the way back to the interpreter awaiting new input,
        /// send that queue out to the interpreter.  This allows you to type the next command
        /// blindly while waiting for the previous one to finish.
        /// This is how people used to terminals would expect things to work.
        /// </summary>
        /// <returns>how many keys were given to the prompt</returns>
        public int ProcessUnconsumedInput()
        {
            if (!host.HasBooted)
            {
                return 0; // Fix race condition (Github issue #2925) where Update() calls this before FixedUpdate() has set up the CPU.
            }
            IScreenBuffer screen = host.Screen;
            if (screen == null || screen.CharInputQueue.Count == 0)
                return 0; // nearly always: no budget or closures to make for nothing (P-01)
            IInterpreter interpreter = host.Interpreter;
            if (interpreter != null && interpreter.IsWaitingForCommand())
                return FeedPrompt(screen, interpreter);
            return 0;
        }

        // Apart from ProcessUnconsumedInput, for the reason given at DrainClients.
        private int FeedPrompt(IScreenBuffer screen, IInterpreter interpreter)
        {
            // Setting doQueuing to false here just as an
            // additional safety measure.  Hypothetically it
            // should never re-queue this input because we're
            // in the condition where it will consume it right
            // away.  But just in case there's some error in
            // that logic, we want to avoid an infinite loop here
            // (which could happen if we kept re-queuing the
            // chars every time we processed one in this loop.)
            // The budget keeps a long pasted line from stalling one frame; the rest
            // is typed in the following frames.
            return TerminalInputPump.FeedPrompt(screen.CharInputQueue, () => interpreter.IsWaitingForCommand(),
                key => ProcessOneInputChar(key, null, false, false),
                new InputBudget(PromptCharsPerUpdate, TimePerUpdate));
        }

        /// <summary>
        /// Respond to one single input character in Unicode, using the pretend
        /// virtual Unicode terminal keycodes described in the UnicodeCommand enum.
        /// To keep things simple, all key input is coerced into single Unicode chars
        /// even if the actual keypress takes multiple characters to express in its
        /// native form (i.e. ESC [ A means left-arrow on a VT100 terminal.  If
        /// a telnet is talking via VT100 codes, that ESC [ A will get converted into
        /// a single UnicdeCommand.LEFTCURSORONE character before being sent here.)
        /// </summary>
        /// <param name="ch">The character, which might be a UnicodeCommand char</param>
        /// <param name="whichClient">If this came from a telnet session, which one did it come from?
        /// Null if it wasn't from a telnet but was from the interactive GUI (or a mod).</param>
        /// <param name="allowQueue">true if the keypress should get queued if we're not ready for it
        /// right now.  If false, then the keypress will be ignored if we're not ready for it.</param>
        /// <param name="forceQueue">true if the keypress MUST get queued even if we are ready for it.
        /// Use this for input that can jam in quickly faster than the interpeter could respond, to ensure it
        /// doesn't get the order mixed up.  (I.e. use it for paste buffer dumps or telnet input, but not
        /// live GUI typed stuff.)</param>
        /// <returns>True if the input got consuemed or enqueued.  If the input was blocked and not ignored, it returns false.</returns>
        public bool ProcessOneInputChar(char ch, TClient whichClient, bool allowQueue, bool forceQueue)
        {
            // Weird exceptions for multi-char data combos that would have been begun on previous calls to this method:
            switch (inputExpected)
            {
                case ExpectNextChar.RESIZEWIDTH:
                    pendingWidth = ch;
                    inputExpected = ExpectNextChar.RESIZEHEIGHT;
                    return true;
                case ExpectNextChar.RESIZEHEIGHT:
                    int height = ch;
                    host.Screen.SetSize(height, pendingWidth);
                    inputExpected = ExpectNextChar.NORMAL;
                    return true;
                default:
                    break;
            }

            if (! IsSpecial(ch))
            {
                 return Type(ch, allowQueue, forceQueue);
            }
            else
            {
                switch(ch)
                {
                    // WARNING: CHARACTERS IN THIS SECTION ARE BYPASSING THE QUEUE.  BUT THEY ARE
                    // WEIRD ASYNC THINGS THAT PROBABLY SHOULD:

                    // A few conversions from UnicodeCommand into those parts of ASCII that it
                    // maps directly into nicely, otherwise just pass it through to SpecialKey():

                    case (char)UnicodeCommand.DELETELEFT:
                    case (char)8:
                        Type((char)8, allowQueue, forceQueue);
                        break;
                    case (char)UnicodeCommand.STARTNEXTLINE:
                    case '\r':
                        Type('\r', allowQueue, forceQueue);
                        break;
                    case '\t':
                        Type('\t', allowQueue, forceQueue);
                        break;
                    case (char)UnicodeCommand.RESIZESCREEN:
                        inputExpected = ExpectNextChar.RESIZEWIDTH;
                        break; // next expected char is the width.

                    // Finish session:  If GUI, then close window.  If Telnet, then detatch from the session.
                    // (The GUI window itself sends Enter for Ctrl-D/Ctrl-X, as upstream does: decision D4;
                    // only a mod sending these codes with no client closes it.)

                    case (char)0x0004/*control-D*/: // How users of unix shells are used to doing this.
                    case (char)0x0018/*control-X*/: // How kOS did it in the past in the GUI window.
                        if (host.Interpreter != null && host.Interpreter.IsAtStartOfCommand()) // null-checked as the other paths are (R-05)
                        {
                            if (whichClient == null)
                                host.CloseWindow();
                            else
                                host.DetachClient(whichClient);
                        }
                        break;

                    // User asking for redraw (Unity already requires that we continually redraw the GUI Terminal, so this is only meaningful for telnet):

                    case (char)UnicodeCommand.REQUESTREPAINT:
                        if (whichClient != null)
                            host.RepaintClient(whichClient);
                        break;

                    // Typical case is to just let SpecialKey do the work:

                    default:
                        return SpecialKey(ch, allowQueue, forceQueue);
                }
                return true;
            }

            // else ignore it - unimplemented char.
        }

        /// <summary>
        /// Queue a prepared paste behind the type-ahead (Phase 3, P3-2): the prompt types it
        /// within its per-update budget, a running script's <c>GETCHAR</c> reads it, and BREAK
        /// wipes what is left. Its keys bypass <see cref="ProcessOneInputChar"/>, so a paste
        /// cannot be taken as part of a resize combo. One key click.
        /// </summary>
        /// <returns>false when nothing was queued (refused, empty, or no screen)</returns>
        public bool QueuePaste(TerminalPaste paste)
        {
            if (paste == null) throw new ArgumentNullException("paste");
            IScreenBuffer screen = host.Screen;
            if (screen == null || paste.IsTooLarge || paste.IsEmpty)
                return false;
            foreach (char key in paste.Keys)
                screen.CharInputQueue.Enqueue(key);
            host.KeyClick();
            return true;
        }

        /// <summary>
        /// Throws away everything in the typeahead buffer and any state vars from multi-char combo command
        /// sequences we might be in the middle of.  Used when you want to fully flush and start input over.
        /// </summary>
        public void WipeAllTypeAhead()
        {
            host.Screen.CharInputQueue.Clear();
            inputExpected = ExpectNextChar.NORMAL;
        }

        private static bool IsSpecial(char c)
        {
            if (c < 0x0020 || c > 0xE000)
                return true;
            if (Enum.IsDefined(typeof(UnicodeCommand), (int)c))
                return true;
            return false;
        }

        /// <summary>
        /// Type a normal unicode char (not a magic control char) to the terminal,
        /// or if the interpreter is busy queue it for later if flags allow.
        /// </summary>
        /// <param name="ch">char to type</param>
        /// <param name="doQueuing">true if you want to queue the char when the interpreter isn't at the prompt
        /// accepting input yet (i.e. the prev command is still running.) if false it will just do nothing in
        /// that case.</param>
        /// <param name="forceQueue">true if you want to queue the char even when the interpreter could have accepted
        /// it.  (never have this true if doQueueing is false.)</param>
        /// <returns>true if the char got either used or queued.  False means calling the method had no effect.
        /// This can only return false when doQueuing is false.</returns>
        private bool Type(char ch, bool doQueuing, bool forceQueue)
        {
            bool accepted = false;
            IScreenBuffer screen = host.Screen;
            if (screen != null)
            {
                IInterpreter interpreter = host.Interpreter;
                // A key that may be queued waits behind queued keys (a paste, telnet input), so
                // it cannot jump ahead of them; the prompt feed itself does not queue.
                bool behindTypeAhead = doQueuing && screen.CharInputQueue.Count > 0;
                if ((!forceQueue) && !behindTypeAhead && interpreter != null && interpreter.IsWaitingForCommand())
                {
                    interpreter.Type(ch);
                    accepted = true;
                }
                else if (doQueuing)
                {
                    screen.CharInputQueue.Enqueue(ch);
                    accepted = true;
                }
                if (doQueuing)
                    host.KeyClick();
            }
            return accepted;
        }

        /// <summary>
        /// Type a special control char to the terminal,
        /// or if the interpreter is busy queue it for later if flags allow.
        /// </summary>
        /// <param name="key">char to type</param>
        /// <param name="doQueuing">true if you want to queue the char when the interpreter isn't at the prompt
        /// accepting input yet (i.e. the prev command is still running.) if false it will just do nothing in
        /// that case. (*EXCEPTION* The BREAK char will bypass the queue and happen right away.)</param>
        /// <param name="forceQueue">true if you want to queue the char even when the interpreter could have accepted
        /// it.  (never have this true if doQueueing is false.)</param>
        /// <returns>true if the char got either used or queued.  False means calling the method had no effect.
        /// This can only return false when doQueuing is false.</returns>
        private bool SpecialKey(char key, bool doQueuing, bool forceQueue)
        {
            bool accepted = false;

            // Force async out of order processing right now for interrupt keys:
            bool rudeQueueSkipping = (key == (char)UnicodeCommand.BREAK);
            if (rudeQueueSkipping)
            {
                forceQueue = false;
                WipeAllTypeAhead();
            }

            IScreenBuffer screen = host.Screen;
            if (screen != null)
            {
                bool wasUsed = false;
                IInterpreter interpreter = host.Interpreter;

                bool behindTypeAhead = doQueuing && screen.CharInputQueue.Count > 0; // as in Type()
                if ((!forceQueue) &&
                    interpreter != null &&
                    ((interpreter.IsWaitingForCommand() && !behindTypeAhead) || rudeQueueSkipping))
                {
                    wasUsed = interpreter.SpecialKey(key);
                    accepted = true;
                }
                else if (doQueuing)
                {
                    screen.CharInputQueue.Enqueue(key);
                    wasUsed = true;
                    accepted = true;
                }
                if (wasUsed && doQueuing)
                    host.KeyClick();
            }
            return accepted;
        }
    }
}
