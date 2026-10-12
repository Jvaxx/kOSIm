using System;
using System.IO;
using kOS.Safe.Exceptions;

namespace kOS.Safe.Persistence
{
    /// <summary>
    /// Prepare a complete sibling file before committing it to the destination.
    /// Relies on filesystem Move/Replace semantics, not a power-loss durability guarantee.
    /// Never falls back to deleting/truncating an existing destination.
    /// </summary>
    internal sealed class ArchiveFileWriter
    {
        private readonly ArchiveFileOperations files;

        internal ArchiveFileWriter(ArchiveFileOperations files)
        {
            if (files == null) throw new ArgumentNullException("files");
            this.files = files;
        }

        internal void Write(string destination, byte[] content)
        {
            string temporary = Path.Combine(Path.GetDirectoryName(destination), ".kosim-save-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool ownsTemporary = false;
            try
            {
                // CreateNew must not truncate an unrelated file even on a name collision.
                using (Stream stream = files.OpenWrite(temporary, FileMode.CreateNew))
                {
                    ownsTemporary = true;
                    stream.Write(content, 0, content.Length);
                    stream.Flush();
                }

                // Close/flush failures prevent commit. Existence is only a routing hint:
                // a newly appeared destination makes Move fail rather than overwrite it;
                // a disappeared destination makes Replace fail rather than recreate it.
                if (files.Exists(destination))
                    files.Replace(temporary, destination);
                else
                    files.Move(temporary, destination);
                ownsTemporary = false; // successful commit consumes the temporary file
            }
            catch (Exception saveError)
            {
                if (ownsTemporary)
                {
                    try
                    {
                        files.Delete(temporary);
                    }
                    catch (Exception cleanupError) when (cleanupError is IOException || cleanupError is UnauthorizedAccessException)
                    {
                        // Do not hide either failure or report success. The path is useful
                        // for manual cleanup; never sweep other temporary files on startup.
                        throw new KOSPersistenceException("Archive save failed for '" + destination +
                            "'; could not remove temporary file '" + temporary + "'.",
                            new AggregateException(saveError, cleanupError));
                    }
                }
                throw;
            }
        }
    }

    // Instance-local seam for deterministic I/O fault tests, not a public mod API or
    // global filesystem override. Production operations use only net48 System.IO.
    internal class ArchiveFileOperations
    {
        internal virtual Stream OpenWrite(string path, FileMode mode)
        {
            return new FileStream(path, mode, FileAccess.Write, FileShare.None);
        }

        internal virtual bool Exists(string path) { return File.Exists(path); }
        internal virtual void Replace(string source, string destination) { File.Replace(source, destination, null); }
        internal virtual void Move(string source, string destination) { File.Move(source, destination); }
        internal virtual void Delete(string path) { File.Delete(path); }
    }
}
