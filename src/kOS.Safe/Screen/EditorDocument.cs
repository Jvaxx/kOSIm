using System;
using kOS.Safe.Exceptions;
using kOS.Safe.Persistence;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// Owning-thread text document transactions, independent of window visibility/focus.
    /// Uses the existing volume text APIs; does not make archive writes atomic or detect
    /// external changes. A pending request is retained after failure until retried/cancelled.
    /// </summary>
    public sealed class EditorDocument
    {
        private GlobalPath activePath;
        private GlobalPath pendingPath;
        private string contents = "";
        private string savedContents = "";

        public Volume ActiveVolume { get; private set; }
        public GlobalPath ActivePath { get { return CopyPath(activePath); } }
        public Volume PendingVolume { get; private set; }
        public GlobalPath PendingPath { get { return CopyPath(pendingPath); } }
        public bool HasPendingOpen { get { return pendingPath != null; } }
        public bool IsNewFile { get; private set; }
        public bool IsDirty { get { return !String.Equals(contents, savedContents, StringComparison.Ordinal); } }

        public string Contents
        {
            get { return contents; }
            set
            {
                if (HasPendingOpen)
                    throw new InvalidOperationException("Resolve or cancel the pending open before editing.");
                contents = value ?? "";
            }
        }

        /// <returns>True if loaded immediately; false if dirty and confirmation is needed.</returns>
        public bool RequestOpen(Volume volume, GlobalPath path)
        {
            if (volume == null) throw new ArgumentNullException("volume");
            if (path == null) throw new ArgumentNullException("path");
            if (HasPendingOpen)
                throw new InvalidOperationException("Resolve or cancel the previous open request first.");

            PendingVolume = volume;
            pendingPath = CopyPath(path);
            if (IsDirty) return false;
            LoadPending();
            return true;
        }

        public void Save()
        {
            if (ActiveVolume == null || activePath == null)
                throw new InvalidOperationException("No active document to save.");

            VolumeFile file = ActiveVolume.SaveFile(activePath, new FileContent(contents));
            if (file == null)
                throw new KOSPersistenceException("File save failed for '" + activePath + "'. Check space on device.");

            activePath = GlobalPath.FromVolumePath(file.Path, activePath.VolumeId);
            savedContents = contents;
            IsNewFile = false;
        }

        /// <summary>Save only the active destination, then attempt the pending load.</summary>
        public void SaveThenOpen()
        {
            RequirePending();
            Save();
            LoadPending();
        }

        /// <summary>
        /// Read/validate everything before replacing active state. A failed read (including
        /// a directory destination) preserves the old text, baseline, location and request.
        /// A missing file remains the legacy empty, clean new document.
        /// </summary>
        public void LoadPending()
        {
            RequirePending();
            VolumeItem item = PendingVolume.Open(pendingPath);
            string newContents = "";
            GlobalPath newPath = pendingPath;
            if (item != null)
            {
                VolumeFile file = item as VolumeFile;
                if (file == null)
                    throw new KOSPersistenceException("Path '" + pendingPath + "' points to a directory");
                newContents = file.ReadAll().String;
                newPath = GlobalPath.FromVolumePath(item.Path, pendingPath.VolumeId);
            }

            ActiveVolume = PendingVolume;
            activePath = newPath;
            contents = newContents;
            savedContents = newContents;
            IsNewFile = item == null;
            CancelOpen();
        }

        public void CancelOpen()
        {
            PendingVolume = null;
            pendingPath = null;
        }

        /// <summary>Explicit discard on exit, not an incidental view-close operation.</summary>
        public void DiscardChanges()
        {
            CancelOpen();
            contents = savedContents;
        }

        private void RequirePending()
        {
            if (!HasPendingOpen)
                throw new InvalidOperationException("No pending document to open.");
        }

        // GlobalPath exposes a mutable segment list. Keep transaction destinations stable.
        private static GlobalPath CopyPath(GlobalPath path)
        {
            return path == null ? null : GlobalPath.FromVolumePath(path, path.VolumeId);
        }
    }
}
