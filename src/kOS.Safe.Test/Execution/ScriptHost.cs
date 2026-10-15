using kOS.Safe.Binding;
using kOS.Safe.Compilation;
using kOS.Safe.Compilation.KS;
using kOS.Safe.Encapsulation;
using kOS.Safe.Execution;
using kOS.Safe.Function;
using kOS.Safe.Module;
using kOS.Safe.Persistence;
using kOS.Safe.Screen;
using kOS.Safe.Sound;
using kOS.Safe.UserIO;
using kOS.Safe.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace kOS.Safe.Test.Execution
{
    /// <summary>
    /// Runs kerboscript the way the in-game terminal does, headlessly: commands are typed
    /// at the real <see cref="TextEditor"/> prompt line, compiled into the interpreter
    /// context, and executed by the real CPU, which prints into the same screen model the
    /// terminal window and telnet draw. The archive is a disposable copy of
    /// <c>kerboscript_tests</c>.
    /// <br/>
    /// Game time advances only by fixed updates of <see cref="UpdateSeconds"/>, so
    /// <c>WAIT</c> and triggers are deterministic.
    /// <br/>
    /// Parts of the game assembly are replaced by test adapters:
    /// <see cref="FixtureInterpreter"/> (the game's <c>Interpreter</c>),
    /// <see cref="FixtureBindingManager"/> (only <c>TERMINAL</c>),
    /// <see cref="FixtureFunctionClearScreen"/> (<c>CLEARSCREEN</c>), and
    /// <see cref="RecordingLogger"/>, which records the errors the game prints.
    /// </summary>
    internal sealed class ScriptHost : IDisposable
    {
        public const double UpdateSeconds = 0.02;

        private readonly SafeSharedObjects shared;
        private readonly CPU cpu;
        private readonly RecordingLogger logger = new RecordingLogger();
        private readonly FixtureProcessor processor = new FixtureProcessor();
        private bool booted;

        public FixtureInterpreter Screen { get; private set; }
        /// <summary>The real compiler; can hold background (non-interpreter) compiles at a gate.</summary>
        public GatedScript Compiler { get; private set; }
        public string ArchiveRoot { get; private set; }
        public IList<Exception> Errors { get { return logger.Errors; } }
        /// <summary>The CPU's volumes; the archive is volume 0. Add disks before typing.</summary>
        public IVolumeManager Volumes { get { return shared.VolumeMgr; } }

        public ScriptHost(int rows = 0, int columns = 0)
        {
            ArchiveRoot = Path.Combine(Path.GetTempPath(), "kosim-fixture-" + Guid.NewGuid().ToString("N"));
            CopyDirectory(FindKerboscriptTests(), ArchiveRoot);
            // KSM packing tables, set up by kOSProcessor in the game:
            kOS.Safe.Compilation.Opcode.InitMachineCodeData();
            CompiledObject.InitTypeData();

            shared = new SafeSharedObjects();
            Screen = new FixtureInterpreter(shared);
            Screen.SetSize(rows, columns); // zero keeps the default axis

            shared.FunctionManager = new FunctionManager(shared);
            shared.GameEventDispatchManager = new NoopGameEventDispatchManager();
            shared.Processor = processor;
            Compiler = new GatedScript();
            shared.ScriptHandler = Compiler;
            shared.Screen = Screen;
            shared.Interpreter = Screen;
            shared.UpdateHandler = new UpdateHandler();
            shared.VolumeMgr = new VolumeManager();
            shared.Logger = logger;
            shared.SoundMaker = new SilentSoundMaker(); // a manual break stops all voices
            shared.BindingMgr = new FixtureBindingManager(shared);

            shared.FunctionManager.Load();

            var archive = new Archive(ArchiveRoot);
            shared.VolumeMgr.Add(archive);
            shared.VolumeMgr.SwitchTo(archive);

            cpu = new CPU(shared);
            shared.Cpu = cpu;
        }

        /// <summary>
        /// Boot the CPU and clear its banner. A boot file is queued by the CPU as in the
        /// game; run updates to execute it.
        /// </summary>
        public void Boot(string bootFile = null)
        {
            processor.BootFile = bootFile == null ? null : VolumePath.FromString(bootFile);
            cpu.Boot();
            Screen.ClearScreen();
            booted = true;
        }

        /// <summary>Type at the prompt, key by key; a newline is the Enter key.</summary>
        public void Type(string keys)
        {
            if (!booted)
                Boot();
            foreach (char ch in keys)
                Screen.Type(ch == '\n' ? '\r' : ch);
        }

        /// <summary>Type one command, press Enter and run until the prompt is idle again.</summary>
        public void Execute(string command, int maxUpdates = 20000)
        {
            Type(command + "\n");
            RunUntilPrompt(maxUpdates);
        }

        /// <summary>
        /// Type one command on a cleared screen, run it, and return the transcript after the
        /// echoed command: the output, as the player sees it below the command.
        /// </summary>
        public string Output(string command, int maxUpdates = 20000)
        {
            Screen.ClearScreen();
            Execute(command, maxUpdates);
            string[] rows = Transcript().Split('\n');
            int echoRows = (command.Length + Screen.ColumnCount - 1) / Screen.ColumnCount;
            if (string.Join("", rows.Take(echoRows).ToArray()) != command)
                throw new Exception("The prompt did not echo the command:\n" + Transcript());
            return string.Join("\n", rows.Skip(echoRows).ToArray());
        }

        /// <summary>Run at the start of every update, e.g. a <see cref="FixtureTermWindow"/>'s input work.</summary>
        public Action BeforeUpdate { get; set; }

        public void Update()
        {
            if (BeforeUpdate != null)
                BeforeUpdate();
            shared.UpdateHandler.UpdateFixedObservers(UpdateSeconds);
        }

        /// <summary>
        /// Step fixed updates until the interpreter waits for a command with nothing
        /// pending. Compiles run on a background thread, so an update that ran no
        /// instructions yields briefly to it.
        /// </summary>
        public void RunUntilPrompt(int maxUpdates = 20000)
        {
            for (int i = 0; i < maxUpdates; ++i)
            {
                bool idleBefore = Screen.IsWaitingForCommand();
                Update();
                // Only the end-of-input opcode ran: no program, yield or trigger is pending.
                if (idleBefore && Screen.IsWaitingForCommand() && cpu.InstructionsThisUpdate == 1)
                    return;
                if (cpu.InstructionsThisUpdate == 0)
                    Thread.Sleep(1);
            }
            throw new Exception("Script did not return to the prompt within " + maxUpdates + " updates\n" + State());
        }

        private string State()
        {
            var context = (ProgramContext)cpu.GetCurrentContext();
            string opcode = context.InstructionPointer < context.Program.Count ? context.Program[context.InstructionPointer].ToString() : "(end)";
            return string.Format("context {0}, IP {1}: {2}; waiting for command {3}; {4} error(s), first: {5}\nTranscript:\n{6}",
                context == cpu.GetInterpreterContext() ? "interpreter" : "program", context.InstructionPointer, opcode,
                Screen.IsWaitingForCommand(), Errors.Count, Errors.Count > 0 ? Errors[0].ToString() : "none", Transcript());
        }

        /// <summary>Step until <paramref name="condition"/> holds, e.g. a first frame is drawn.</summary>
        public void RunUntil(Func<bool> condition, int maxUpdates = 20000)
        {
            for (int i = 0; i < maxUpdates; ++i)
            {
                if (condition())
                    return;
                Update();
                if (cpu.InstructionsThisUpdate == 0)
                    Thread.Sleep(1);
            }
            throw new Exception("Condition not reached within " + maxUpdates + " updates\n" + State());
        }

        /// <summary>
        /// Step until <paramref name="idleUpdates"/> consecutive updates run no instructions,
        /// e.g. a dashboard waiting on <c>TERMINAL:INPUT:GETCHAR()</c>, or until the prompt
        /// returns. Only for code that is already compiled: compile time depends on the wall
        /// clock.
        /// </summary>
        public void RunUntilBlocked(int idleUpdates = 10, int maxUpdates = 20000)
        {
            int idle = 0;
            for (int i = 0; i < maxUpdates; ++i)
            {
                bool idleBefore = Screen.IsWaitingForCommand();
                Update();
                if (idleBefore && Screen.IsWaitingForCommand() && cpu.InstructionsThisUpdate == 1)
                    return;
                idle = cpu.InstructionsThisUpdate == 0 ? idle + 1 : 0;
                if (idle >= idleUpdates)
                    return;
            }
            throw new Exception("Script did not block within " + maxUpdates + " updates\n" + State());
        }

        /// <summary>Keys for a running script, queued as the terminal window and telnet queue them.</summary>
        public void PressKeys(string keys)
        {
            foreach (char ch in keys)
                Screen.CharInputQueue.Enqueue(ch);
        }

        public void AssertNoErrors()
        {
            if (Errors.Count > 0)
                throw new Exception("Script raised " + Errors.Count + " error(s):\n" +
                    string.Join("\n", Errors.Select(e => e.GetType().Name + ": " + e.Message).ToArray()) +
                    "\nTranscript:\n" + Transcript());
        }

        /// <summary>
        /// All output rows, scrollback included, with trailing blanks trimmed and trailing
        /// empty rows dropped. The viewport and input line are restored afterward.
        /// <br/>
        /// The prompt's input line is hidden while reading: it overlays the cursor row, which
        /// is viewport-relative, so scrolling back would draw it over older output.
        /// </summary>
        public string Transcript()
        {
            bool wasLocked = Screen.InputLocked;
            Screen.SetInputLock(true);
            try { return ReadAllRows(); }
            finally { Screen.SetInputLock(wasLocked); }
        }

        private string ReadAllRows()
        {
            int originalTop = Screen.TopRow;
            Screen.ScrollVertical(-originalTop);
            var rows = new List<string>();
            int nextAbsoluteRow = 0;
            while (true)
            {
                List<IScreenBufferLine> view = Screen.GetBuffer();
                for (int i = nextAbsoluteRow - Screen.TopRow; i < view.Count; ++i)
                    rows.Add(view[i].ToString().Replace('\0', ' ').TrimEnd());
                nextAbsoluteRow = Screen.TopRow + view.Count;
                if (Screen.ScrollVertical(Screen.RowCount) == 0)
                    break;
            }
            Screen.ScrollVertical(originalTop - Screen.TopRow);
            while (rows.Count > 0 && rows[rows.Count - 1].Length == 0)
                rows.RemoveAt(rows.Count - 1);
            return string.Join("\n", rows.ToArray());
        }

        /// <summary>
        /// The visible grid, one row per line between <c>|</c> marks so trailing blanks
        /// show, followed by the cursor position the terminal window draws.
        /// </summary>
        public string Snapshot()
        {
            var sb = new StringBuilder();
            foreach (IScreenBufferLine line in Screen.GetBuffer())
                sb.Append('|').Append(line.ToString().Replace('\0', ' ')).Append("|\n");
            sb.AppendFormat("cursor {0},{1}", Screen.CursorRowShow, Screen.CursorColumnShow);
            return sb.ToString();
        }

        public byte[] ReadArchiveBytes(string relativePath)
        {
            return File.ReadAllBytes(Path.Combine(ArchiveRoot, relativePath));
        }

        public void WriteArchiveBytes(string relativePath, byte[] bytes)
        {
            string fullPath = Path.Combine(ArchiveRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, bytes);
        }

        public void Dispose()
        {
            Compiler.Release(); // never leave a compile thread parked at the gate
            if (Directory.Exists(ArchiveRoot))
                Directory.Delete(ArchiveRoot, true);
        }

        public static string FindKerboscriptTests()
        {
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            while (!Directory.Exists(Path.Combine(currentDir, "kerboscript_tests")))
                currentDir = Directory.GetParent(currentDir).FullName;
            return Path.Combine(currentDir, "kerboscript_tests");
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }

    /// <summary>
    /// A copy of the game's <c>kOS.Screen.Interpreter</c> (game assembly, not loadable
    /// headlessly) on the real <see cref="TextEditor"/>: the same Enter handling, command
    /// history for error messages, input lock and cursor-preserving <c>PRINT AT</c>. History
    /// navigation keys are left out. Keep it in step with
    /// <c>src/kOS/Screen/Interpreter.cs</c>; only window and logger access differ.
    /// </summary>
    internal sealed class FixtureInterpreter : TextEditor, IInterpreter
    {
        private const string InterpreterName = "interpreter";
        private readonly List<string> commandHistory = new List<string>();
        private readonly SafeSharedObjects shared;
        private int commandHistoryIndex;
        private bool locked;

        public bool InputLocked { get { return locked; } }

        public FixtureInterpreter(SafeSharedObjects shared)
        {
            this.shared = shared;
        }

        protected override void NewLine()
        {
            string commandText = LineBuilder.ToString();

            if (shared.ScriptHandler.IsCommandComplete(commandText))
                EndLine(commandText, true);
            else
                InsertChar('\n');
        }

        private void EndLine(string commandText, bool compile)
        {
            base.NewLine();
            AddCommandHistoryEntry(commandText);
            if (compile)
                CompileCommand(commandText);
            int numRows = LineSubBuffer.RowCount;
            LineSubBuffer.Wipe();
            LineSubBuffer.SetSize(numRows, ColumnCount);
        }

        public bool IsAtStartOfCommand()
        {
            return LineBuilder == null || LineBuilder.Length == 0;
        }

        public override void Type(char ch)
        {
            if (!locked)
                base.Type(ch);
        }

        public override bool SpecialKey(char key)
        {
            if (key == (char)UnicodeCommand.BREAK)
            {
                shared.Cpu.BreakExecution(true);
                LineBuilder.Remove(0, LineBuilder.Length);
                EndLine(string.Empty, false); // as the game's Interpreter: no compile of the emptied line
            }
            if (locked) return false;
            return base.SpecialKey(key);
        }

        private void AddCommandHistoryEntry(string commandText)
        {
            if (commandHistory.Count == 0 || commandText != commandHistory[commandHistory.Count - 1])
                commandHistory.Add(commandText);
            commandHistoryIndex = commandHistory.Count;
        }

        public string GetCommandHistoryAbsolute(int absoluteIndex)
        {
            return commandHistory[absoluteIndex - 1];
        }

        private void CompileCommand(string commandText)
        {
            try
            {
                var options = new CompilerOptions
                {
                    LoadProgramsInSameAddressSpace = false,
                    FuncManager = shared.FunctionManager,
                    BindManager = shared.BindingMgr,
                    AllowClobberBuiltins = SafeHouse.Config.AllowClobberBuiltIns,
                    IsCalledFromRun = false
                };
                List<CodePart> commandParts = shared.ScriptHandler.Compile(new InterpreterPath(this),
                    commandHistoryIndex, commandText, InterpreterName, options);
                if (commandParts == null) return;
                ((CPU)shared.Cpu).GetInterpreterContext().AddParts(commandParts);
            }
            catch (Exception e)
            {
                shared.Logger.Log(e);
            }
        }

        public bool IsWaitingForCommand()
        {
            var cpu = (CPU)shared.Cpu;
            if (cpu.IsFinishingCancelledCompile)
                return false; // as the game's Interpreter
            IProgramContext context = cpu.GetInterpreterContext();
            return !locked && context.Program[context.InstructionPointer] is OpcodeEOF;
        }

        public void SetInputLock(bool isLocked)
        {
            locked = isLocked;
            LineSubBuffer.Enabled = !isLocked;
        }

        public override void Reset()
        {
            shared.ScriptHandler.ClearContext(InterpreterName);
            commandHistory.Clear();
            commandHistoryIndex = 0;
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
            private readonly FixtureInterpreter interpreter;

            public InterpreterPath(FixtureInterpreter interpreter)
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

    /// <summary>
    /// The real kerboscript compiler. With <see cref="Hold"/> set, a background compile
    /// (any context but the interpreter's: COMPILE, RUNPATH, boot) waits at the gate until
    /// <see cref="Release"/>, so a test can act while the compile thread is still running.
    /// Records whether two compiles ever ran at once: the compiler is not thread-safe.
    /// </summary>
    internal sealed class GatedScript : KSScript
    {
        private const string InterpreterName = "interpreter";
        private readonly ManualResetEvent gate = new ManualResetEvent(true);
        private readonly ManualResetEvent entered = new ManualResetEvent(false);
        private int running;
        private int overlaps;

        public int Overlaps { get { return Thread.VolatileRead(ref overlaps); } }
        public readonly List<string> OverlapLog = new List<string>();
        private string activeContext;

        public void Hold()
        {
            entered.Reset();
            gate.Reset();
        }

        public void Release()
        {
            gate.Set();
        }

        /// <summary>Wait until a held background compile has reached the gate.</summary>
        public bool WaitUntilHeld(int milliseconds = 5000)
        {
            return entered.WaitOne(milliseconds);
        }

        public override List<CodePart> Compile(GlobalPath filePath, int startLineNum, string scriptText, string contextId, CompilerOptions options)
        {
            if (Interlocked.Increment(ref running) > 1)
            {
                Interlocked.Increment(ref overlaps);
                lock (OverlapLog) OverlapLog.Add("[" + contextId + "] while [" + activeContext + "]: " + Environment.StackTrace);
            }
            activeContext = contextId;
            try
            {
                if (contextId != InterpreterName)
                {
                    entered.Set();
                    gate.WaitOne();
                }
                return base.Compile(filePath, startLineNum, scriptText, contextId, options);
            }
            finally
            {
                Interlocked.Decrement(ref running);
            }
        }
    }

    /// <summary>Records the script errors that the game's logger prints on the terminal.</summary>
    internal sealed class RecordingLogger : ILogger
    {
        public readonly List<Exception> Errors = new List<Exception>();

        public void Log(Exception e) { Errors.Add(e); }
        public void Log(string text) { }
        public void LogError(string s) { Errors.Add(new Exception(s)); }
        public void LogException(Exception exception) { Errors.Add(exception); }
        public void LogWarning(string s) { }
        public void LogWarningAndScreen(string s) { }
        public void SuperVerbose(string s) { }
    }

    /// <summary>The game's sound maker, without sound.</summary>
    internal sealed class SilentSoundMaker : ISoundMaker
    {
        public bool BeginFileSound(string soundName, float volume = 1f) { return true; }
        public IVoice GetVoice(int num) { return null; }
        public void StopAllVoices() { }
        public string GetWaveName(int voiceNum) { return string.Empty; }
        public bool SetWave(int num, string waveName) { return false; }
    }

    internal sealed class FixtureProcessor : IProcessor
    {
        public VolumePath BootFile { get; set; }
        public VolumePath BootFilePath { get { return BootFile; } }
        public int KOSCoreId { get { return 0; } }
        public string Tag { get { return string.Empty; } }
        public bool CheckCanBoot() { return true; }
        public bool HasBooted { get { return true; } }
        public void SetMode(ProcessorModes newProcessorMode) { }
    }

    /// <summary>
    /// The game's <c>BindingManager</c>, reduced to the real <see cref="TerminalStruct"/>
    /// behind <c>TERMINAL</c>, the only binding these fixtures use.
    /// </summary>
    internal sealed class FixtureBindingManager : IBindingManager
    {
        private readonly SafeSharedObjects shared;
        private readonly Dictionary<string, BoundVariable> variables = new Dictionary<string, BoundVariable>(StringComparer.OrdinalIgnoreCase);
        private TerminalStruct terminal;

        public FixtureBindingManager(SafeSharedObjects shared)
        {
            this.shared = shared;
        }

        public void Load()
        {
            variables.Clear();
            AddGetter("TERMINAL", () => terminal ?? (terminal = new TerminalStruct(shared)));
        }

        public void AddBoundVariable(string name, BindingGetDlg getDelegate, BindingSetDlg setDelegate)
        {
            BoundVariable variable;
            if (!variables.TryGetValue(name, out variable))
            {
                variable = new BoundVariable { Name = name };
                variables.Add(name, variable);
                shared.Cpu.AddVariable(variable, name, false);
            }
            if (getDelegate != null)
                variable.Get = getDelegate;
            if (setDelegate != null)
                variable.Set = setDelegate;
        }

        public void AddGetter(string name, BindingGetDlg dlg) { AddBoundVariable(name, dlg, null); }
        public void AddGetter(IEnumerable<string> names, BindingGetDlg dlg) { foreach (string name in names) AddGetter(name, dlg); }
        public void AddSetter(string name, BindingSetDlg dlg) { AddBoundVariable(name, null, dlg); }
        public void AddSetter(IEnumerable<string> names, BindingSetDlg dlg) { foreach (string name in names) AddSetter(name, dlg); }
        public bool HasGetter(string name) { BoundVariable v; return variables.TryGetValue(name, out v) && v.Get != null; }
        public bool HasSetter(string name) { BoundVariable v; return variables.TryGetValue(name, out v) && v.Set != null; }
        public void MarkVolatile(string name) { }
        public void PreUpdate() { }
        public void PostUpdate() { }
        public void ToggleFlyByWire(string paramName, bool enabled) { }
        public void SelectAutopilotMode(string autopilotMode) { }
    }

    /// <summary>
    /// Test stand-in for the game's <c>CLEARSCREEN</c>, which calls
    /// <c>TermWindow.ClearScreen()</c>: that clears this same screen model, then tells
    /// attached telnet clients, of which a headless run has none.
    /// </summary>
    [Function("clearscreen")]
    public class FixtureFunctionClearScreen : SafeFunctionBase
    {
        public override void Execute(SafeSharedObjects shared)
        {
            AssertArgBottomAndConsume(shared);
            shared.Screen.ClearScreen();
        }
    }
}
