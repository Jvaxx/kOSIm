using System;
using System.Collections.Generic;
using System.Text;
using kOS.Execution;
using kOS.Safe.Compilation;
using kOS.Safe.Execution;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using kOS.Safe.Persistence;
using kOS.Safe.Utilities;

namespace kOS.Screen
{
    public class Interpreter : CommandLineEditor, IInterpreter
    {
        public const string InterpreterName = "interpreter";
        private int commandFirstLine;
        private bool locked;

        protected SharedObjects Shared { get; private set; }

        public Interpreter(SharedObjects shared)
        {
            Shared = shared;
        }

        protected override void NewLine()
        {
            string commandText = LineBuilder.ToString();

            if (Shared.ScriptHandler.IsCommandComplete(commandText))
                EndLine(commandText, true);
            else
                InsertChar('\n');
        }

        private void EndLine(string commandText, bool process)
        {
            base.NewLine();
            AddCommandHistoryEntry(commandText); // add to history first so that if ProcessCommand generates an exception,
                                                 // the command is present in the history to be found and printed in the
                                                 // error message.
            if (process)
                ProcessCommand(commandText);
            int numRows = LineSubBuffer.RowCount;
            LineSubBuffer.Wipe();
            LineSubBuffer.SetSize(numRows,ColumnCount); // refill it to its previous size
        }
        
        /// <summary>
        /// Detect if the interpreter happens to be right at the start of a new command line.
        /// </summary>
        /// <returns>true if it's at the start of a new line</returns>
        public bool IsAtStartOfCommand()
        {
            // Not during a history search: Ctrl-D there must not detach or close.
            return !IsSearchingHistory && (LineBuilder == null || LineBuilder.Length == 0);
        }

        public override void Type(char ch)
        {
            if (!locked)
            {
                base.Type(ch);
            }
        }

        public override bool SpecialKey(char key)
        {
            if (key == (char)UnicodeCommand.BREAK && !IsSearchingHistory) // in a search, BREAK only leaves it
            {
                Shared.Cpu.BreakExecution(true);
                LineBuilder.Remove(0, LineBuilder.Length); // why isn't there a StringBuilder.Clear()?

                // Do all the updates pressing enter does to the screenbuffers, but leave the
                // emptied line out of the compiler: it compiles to nothing, and a cancelled
                // compile thread may still be using the compiler, which is not thread-safe.
                EndLine(string.Empty, false);
            }

            if (locked) return false;

            return base.SpecialKey(key); // the history keys (Up/Down, Ctrl+R) and line editing
        }

        private void AddCommandHistoryEntry(string commandText)
        {
            commandFirstLine = RecordCommand(commandText); // saved when it has a store
        }

        public string GetCommandHistoryAbsolute(int absoluteIndex)
        {
            return History.GetLine(absoluteIndex);
        }

        protected virtual void ProcessCommand(string commandText)
        {
            CompileCommand(commandText);
        }

        protected void CompileCommand(string commandText)
        {
            if (Shared.ScriptHandler == null) return;

            try
            {
                CompilerOptions options = new CompilerOptions
                {
                    LoadProgramsInSameAddressSpace = false,
                    FuncManager = Shared.FunctionManager,
                    BindManager = Shared.BindingMgr,
                    AllowClobberBuiltins = SafeHouse.Config.AllowClobberBuiltIns,
                    IsCalledFromRun = false
                };

                List<CodePart> commandParts = Shared.ScriptHandler.Compile(new InterpreterPath(this),
                    commandFirstLine, commandText, InterpreterName, options);
                if (commandParts == null) return;

                var interpreterContext = ((CPU)Shared.Cpu).GetInterpreterContext();
                interpreterContext.AddParts(commandParts);
            }
            catch (Exception e)
            {
                if (Shared.Logger != null)
                {
                    Shared.Logger.Log(e);
                }
            }
        }

        public bool IsWaitingForCommand()
        {
            var cpu = (CPU)Shared.Cpu;
            if (cpu.IsFinishingCancelledCompile)
                return false; // Ctrl-C'd COMPILE or RUNPATH: its thread still uses the compiler.
            IProgramContext context = cpu.GetInterpreterContext();
            // If running from a boot script, there will be no interpreter instructions,
            // only a single OpcodeEOF.  So we check to see if the interpreter is locked,
            // which is a sign that a sub-program is running.
            return !locked && context.Program[context.InstructionPointer] is OpcodeEOF;
        }

        public void SetInputLock(bool isLocked)
        {
            locked = isLocked;
            if (Shared.Window != null) Shared.Window.ShowCursor = !isLocked;
            LineSubBuffer.Enabled = !isLocked;
        }

        public override void Reset()
        {
            Shared.ScriptHandler.ClearContext(InterpreterName);
            commandFirstLine = 0; // a reboot keeps the history (user decision, Phase 3 H3)
            base.Reset();
        }

        public override void PrintAt(string textToPrint, int row, int column)
        {
            SaveCursorPos();
            base.PrintAt(textToPrint, row, column);
            RestoreCursorPos();
        }

        private class InterpreterPath : InternalPath
        {
            private Interpreter interpreter;

            public InterpreterPath(Interpreter interpreter) : base()
            {
                this.interpreter = interpreter;
            }

            public override string Line(int line)
            {
                return interpreter.GetCommandHistoryAbsolute(line);
            }

            public override string ToString()
            {
                return InterpreterName;
            }
        }
    }
}
