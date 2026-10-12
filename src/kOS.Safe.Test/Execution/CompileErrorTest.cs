using NUnit.Framework;
using System.IO;
using System.Text;

namespace kOS.Safe.Test.Execution
{
    /// <summary>
    /// EXEC-01: a <c>COMPILE</c> typed at the prompt that failed on the compile thread
    /// re-reported its error on every update, and the prompt never ran another command
    /// until reboot. The failed yield stayed in the CPU's list and its detector rethrew
    /// the stored exception each time it was checked.
    /// </summary>
    [TestFixture]
    public class CompileErrorTest
    {
        private const string BrokenSource = "print \"before\".\nprint (.\n";

        private static ScriptHost BootWithBrokenFile()
        {
            var host = new ScriptHost();
            host.WriteArchiveBytes("exec01/broken.ks", Encoding.UTF8.GetBytes(BrokenSource));
            host.Boot();
            return host;
        }

        [Test]
        public void FailedCompileAtPromptReportsOnceAndFreesThePrompt()
        {
            using (var host = BootWithBrokenFile())
            {
                host.Execute("compile \"0:/exec01/broken.ks\" to \"0:/exec01/broken.ksm\".");
                Assert.AreEqual(1, host.Errors.Count, "errors after the failed compile");

                for (int i = 0; i < 200; ++i)
                    host.Update();
                Assert.AreEqual(1, host.Errors.Count, "the compile error was reported again");

                Assert.AreEqual("after", host.Output("print \"after\"."));
                Assert.AreEqual(1, host.Errors.Count);
                Assert.IsFalse(File.Exists(Path.Combine(host.ArchiveRoot, "exec01", "broken.ksm")), "a failed compile wrote output");
            }
        }

        [Test]
        public void CompileWorksAgainAfterAFailedCompile()
        {
            using (var host = BootWithBrokenFile())
            {
                host.Execute("compile \"0:/exec01/broken.ks\" to \"0:/exec01/broken.ksm\".");
                host.WriteArchiveBytes("exec01/fixed.ks", Encoding.UTF8.GetBytes("print \"fixed\".\n"));
                host.Execute("compile \"0:/exec01/fixed.ks\" to \"0:/exec01/fixed.ksm\".");
                Assert.AreEqual(1, host.Errors.Count);

                Assert.AreEqual("fixed\nProgram ended.", host.Output("runpath(\"0:/exec01/fixed.ksm\")."));
                Assert.AreEqual(1, host.Errors.Count);
            }
        }

        [Test]
        public void CompileErrorKeepsItsOriginalStackTrace()
        {
            using (var host = BootWithBrokenFile())
            {
                host.Execute("compile \"0:/exec01/broken.ks\" to \"0:/exec01/broken.ksm\".");
                Assert.AreEqual(1, host.Errors.Count);
                // Rethrowing with "throw childException" cut the trace off at IsFinished().
                StringAssert.Contains("ThreadExecute", host.Errors[0].StackTrace);
            }
        }

        [Test]
        public void FailedRunpathCompileStillReportsOnce()
        {
            // RUNPATH yields from a program context and was not affected; pin that.
            using (var host = BootWithBrokenFile())
            {
                host.Execute("runpath(\"0:/exec01/broken.ks\").");
                Assert.AreEqual(1, host.Errors.Count);
                for (int i = 0; i < 200; ++i)
                    host.Update();
                Assert.AreEqual(1, host.Errors.Count);
                Assert.AreEqual("after", host.Output("print \"after\"."));
            }
        }
    }
}
