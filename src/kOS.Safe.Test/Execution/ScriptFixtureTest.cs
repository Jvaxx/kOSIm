using kOS.Safe.Persistence;
using kOS.Safe.Utilities;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace kOS.Safe.Test.Execution
{
    /// <summary>
    /// Phase 0 compatibility fixtures: representative scripts under
    /// <c>kerboscript_tests/fixtures</c> run through the real interpreter and screen
    /// buffer, from source and from compiled KSM, and their full transcripts are
    /// compared with recorded output in <c>fixtures/expected</c>.
    /// <br/>
    /// Each script runs with both newline conventions of the archive: Unix, and Windows
    /// (the Steam game under Proton), where text saves add carriage returns. Output that
    /// differs on Windows is recorded in a separate <c>.windows.out</c> file.
    /// <br/>
    /// To re-record after an intended change, run the suite with
    /// <c>KOSIM_RECORD_FIXTURES=1</c>; recording tests fail so a recording run never
    /// passes as a check. Record twice when Unix output changes: a Windows file is kept
    /// only while it differs from the Unix one. Review the diff before committing.
    /// </summary>
    [TestFixture]
    public class ScriptFixtureTest
    {
        private static readonly PropertyInfo WindowsProperty = typeof(SafeHouse).GetProperty("IsWindows");
        private bool previousWindows;

        [SetUp]
        public void SetUp()
        {
            previousWindows = SafeHouse.IsWindows;
        }

        [TearDown]
        public void TearDown()
        {
            SetWindows(previousWindows);
        }

        private static void SetWindows(bool windows)
        {
            WindowsProperty.GetSetMethod(true).Invoke(null, new object[] { windows });
        }

        private static readonly string[] Scripts = { "printing", "functions", "triggers", "fileio" };

        private static readonly object[] ScriptCases = Scripts
            .SelectMany(name => new[] { new object[] { name, false }, new object[] { name, true } })
            .ToArray();

        [TestCaseSource("ScriptCases")]
        public void SourceRunMatchesRecordedOutput(string name, bool windows)
        {
            SetWindows(windows);
            AssertRecorded(name, windows, RunSource(name));
        }

        [TestCaseSource("ScriptCases")]
        public void CompiledRunMatchesSourceRun(string name, bool windows)
        {
            SetWindows(windows);
            byte[] ksm = CompileToKsm("fixtures/" + name + ".ks");
            string compiled = RunCompiled(name, ksm);

            Assert.AreEqual(RunSource(name), compiled, "KSM output differs from source output");
            AssertRecorded(name, windows, compiled);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CrlfBootScriptRunsFromSourceAndKsm(bool windows)
        {
            SetWindows(windows);
            const string bootPath = "fixtures/boot/boot_crlf.ks";
            byte[] bootBytes = File.ReadAllBytes(Path.Combine(ScriptHost.FindKerboscriptTests(), bootPath));
            Assert.That(CountCrlf(bootBytes), Is.GreaterThan(10), "boot fixture lost its CRLF line endings");
            Assert.AreEqual(CountCrlf(bootBytes), bootBytes.Count(b => b == (byte)'\n'), "boot fixture has bare LF line endings");

            string source;
            using (var host = new ScriptHost())
            {
                host.Boot("/" + bootPath);
                host.RunUntilPrompt();
                host.AssertNoErrors();
                source = host.Transcript();
                StringAssert.StartsWith("booted", Encoding.UTF8.GetString(host.ReadArchiveBytes("fixtures/boot.log")));
            }

            byte[] ksm = CompileToKsm(bootPath);
            string compiled;
            using (var host = new ScriptHost())
            {
                host.WriteArchiveBytes("fixtures/boot/boot_crlf.ksm", ksm);
                host.Boot("/fixtures/boot/boot_crlf.ksm");
                host.RunUntilPrompt();
                host.AssertNoErrors();
                compiled = host.Transcript();
            }

            Assert.AreEqual(source, compiled, "KSM boot output differs from source boot output");
            AssertRecorded("boot_crlf", windows, source);
        }

        [Test]
        public void KsmIsRecognizedAsCompiledCode()
        {
            byte[] ksm = CompileToKsm("fixtures/printing.ks");
            Assert.AreEqual(FileCategory.KSM, new FileContent(ksm).Category);
        }

        private static string RunSource(string name)
        {
            using (var host = new ScriptHost())
            {
                host.Boot();
                string output = host.Output("runpath(\"0:/fixtures/" + name + ".ks\").");
                host.AssertNoErrors();
                return output;
            }
        }

        private static string RunCompiled(string name, byte[] ksm)
        {
            using (var host = new ScriptHost())
            {
                // Run it where the source would be, so paths the script uses resolve the same.
                host.WriteArchiveBytes("fixtures/" + name + ".ksm", ksm);
                host.Boot();
                string output = host.Output("runpath(\"0:/fixtures/" + name + ".ksm\").");
                host.AssertNoErrors();
                return output;
            }
        }

        /// <summary>Compile with the script <c>COMPILE</c> command, as players do.</summary>
        private static byte[] CompileToKsm(string relativeSource)
        {
            using (var host = new ScriptHost())
            {
                host.Boot();
                string output = host.Output("compile \"0:/" + relativeSource + "\" to \"0:/compiled.ksm\".");
                host.AssertNoErrors();
                Assert.AreEqual(string.Empty, output, "COMPILE printed output");
                return host.ReadArchiveBytes("compiled.ksm");
            }
        }

        private static int CountCrlf(byte[] bytes)
        {
            int count = 0;
            for (int i = 1; i < bytes.Length; ++i)
                if (bytes[i - 1] == (byte)'\r' && bytes[i] == (byte)'\n')
                    ++count;
            return count;
        }

        internal static void AssertRecorded(string name, bool windows, string actual)
        {
            string dir = Path.Combine(ScriptHost.FindKerboscriptTests(), "fixtures", "expected");
            string unixFile = Path.Combine(dir, name + ".out");
            string windowsFile = Path.Combine(dir, name + ".windows.out");
            string file = windows && File.Exists(windowsFile) ? windowsFile : unixFile;
            string normalized = actual.Replace("\r", "\\r") + "\n";

            if (Environment.GetEnvironmentVariable("KOSIM_RECORD_FIXTURES") == "1")
            {
                if (!windows)
                {
                    File.WriteAllText(unixFile, normalized);
                }
                else if (!File.Exists(unixFile) || File.ReadAllText(unixFile) != normalized)
                {
                    File.WriteAllText(windowsFile, normalized);
                }
                else if (File.Exists(windowsFile))
                {
                    File.Delete(windowsFile);
                }
                Assert.Fail("Recorded " + name + (windows ? " (windows)" : "") + "; rerun without KOSIM_RECORD_FIXTURES to check.");
            }

            Assert.IsTrue(File.Exists(file), "No recorded output " + file);
            Assert.AreEqual(File.ReadAllText(file), normalized, "Output differs from " + Path.GetFileName(file));
        }
    }
}
