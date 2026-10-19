using System;
using System.Collections.Generic;
using kOS.Safe.Encapsulation;
using kOS.Safe.Exceptions;
using kOS.Safe.Utilities;

namespace kOS.Safe.Persistence
{
    public class VolumeManager : IVolumeManager
    {
        private readonly Dictionary<int, Volume> volumes;
        public virtual Volume CurrentVolume { get { return CurrentDirectory != null ? CurrentDirectory.Volume : null; } }
        public VolumeDirectory CurrentDirectory { get; set; }
        private int lastId;

        public Dictionary<int, Volume> Volumes { get { return volumes; } }

        public VolumeManager()
        {
            volumes = new Dictionary<int, Volume>();
            CurrentDirectory = null;
        }


        public bool VolumeIsCurrent(Volume volume)
        {
            return volume == CurrentVolume;
        }

        private int GetVolumeId(string name)
        {
            int volumeId = -1;

            foreach (KeyValuePair<int, Volume> kvp in volumes)
            {
                if (string.Equals(kvp.Value.Name, name, StringComparison.CurrentCultureIgnoreCase))
                {
                    volumeId = kvp.Key;
                    break;
                }
            }

            return volumeId;
        }

        public int GetVolumeId(Volume volume)
        {
            int volumeId = -1;

            foreach (KeyValuePair<int, Volume> kvp in volumes)
            {
                if (kvp.Value == volume)
                {
                    volumeId = kvp.Key;
                    break;
                }
            }

            return volumeId;
        }

        public Volume GetVolume(object volumeId)
        {
            if (volumeId is string || volumeId is StringValue) return GetVolume(volumeId.ToString());
            // Convert to int instead of cast in case the identifier is stored
            // as an encapsulated ScalarValue, preventing an unboxing collision.
            try
            {
                return GetVolume(Convert.ToInt32(volumeId));
            }
            catch (InvalidCastException)
            {
                int id = GetVolumeId(volumeId.ToString());
                if (id >= 0)
                {
                    return GetVolume(id);
                }
                throw new kOS.Safe.Exceptions.KOSCastException(volumeId.GetType().Name, "Scalar|String|Volume");
            }
        }

        public Volume GetVolume(string name)
        {
            return GetVolume(GetVolumeId(name));
        }

        public virtual Volume GetVolume(int id)
        {
            if (volumes.ContainsKey(id))
            {
                return volumes[id];
            }
            return null;
        }

        public void Add(Volume volume)
        {
            if (!volumes.ContainsValue(volume))
            {
                volumes.Add(lastId++, volume);

                if (CurrentDirectory == null)
                {
                    CurrentDirectory = volumes[0].Root;
                }
            }
        }

        public void Remove(string name)
        {
            Remove(GetVolumeId(name));
        }

        public void Remove(int id)
        {
            Volume volume = GetVolume(id);

            if (volume != null)
            {
                volumes.Remove(id);

                if (CurrentVolume == volume)
                {
                    if (volumes.Count > 0)
                    {
                        CurrentDirectory = volumes[0].Root;
                    }
                    else
                    {
                        CurrentDirectory = null;
                    }
                }
            }
        }

        public void SwitchTo(Volume volume)
        {
            CurrentDirectory = volume.Root;
        }

        public void UpdateVolumes(List<Volume> attachedVolumes)
        {
            // Remove volumes that are no longer attached
            var removals = new List<int>();
            foreach (var kvp in volumes)
            {
                if (!(kvp.Value is Archive) && !attachedVolumes.Contains(kvp.Value))
                {
                    removals.Add(kvp.Key);
                }
            }

            foreach (int id in removals)
            {
                volumes.Remove(id);
            }

            // Add volumes that have become attached
            foreach (Volume volume in attachedVolumes)
            {
                if (volume != null && !volumes.ContainsValue(volume))
                {
                    Add(volume);
                }
            }
        }

        public string GetVolumeBestIdentifier(Volume volume)
        {
            int id = GetVolumeId(volume);
            if (!string.IsNullOrEmpty(volume.Name)) return string.Format("#{0}: \"{1}\"", id, volume.Name);
            return "#" + id;
        }

        /// <summary>
        /// Like GetVolumeBestIdentifier, but without the extra string formatting.
        /// </summary>
        /// <param name="volume"></param>
        /// <returns>The Volume's Identifier without pretty formatting</returns>
        public string GetVolumeRawIdentifier(Volume volume)
        {
            int id = GetVolumeId(volume);
            return !string.IsNullOrEmpty(volume.Name) ? volume.Name : id.ToString();
        }

        // Volumes, VolumeItems and strings
        public GlobalPath GlobalPathFromObject(object pathObject)
        {
            if (pathObject is Volume)
            {
                GlobalPath p = GlobalPath.FromVolumePath(VolumePath.EMPTY, GetVolumeRawIdentifier(pathObject as Volume));
                SafeHouse.Logger.Log("Path from volume: " + p);
                return p;
            }
            else if (pathObject is VolumeItem)
            {
                VolumeItem volumeItem = pathObject as VolumeItem;
                return GlobalPath.FromVolumePath(volumeItem.Path, GetVolumeRawIdentifier(volumeItem.Volume));
            }
            else
            {
                return GlobalPathFromString(pathObject.ToString());
            }

        }

        // Handles global, absolute and relative paths
        private GlobalPath GlobalPathFromString(string pathString)
        {
            if (GlobalPath.HasVolumeId(pathString))
            {
                return GlobalPath.FromString(pathString);
            }
            else
            {
                if (GlobalPath.IsAbsolute(pathString))
                {
                    return GlobalPath.FromVolumePath(VolumePath.FromString(pathString),
                        GetVolumeRawIdentifier(CurrentVolume));
                }
                else
                {
                    return GlobalPath.FromStringAndBase(pathString, GlobalPath.FromVolumePath(CurrentDirectory.Path,
                        GetVolumeRawIdentifier(CurrentVolume)));
                }
            }

        }

        public Volume GetVolumeFromPath(GlobalPath path)
        {
            Volume volume = GetVolume(path.VolumeId);

            if (volume == null)
            {
                throw new KOSPersistenceException("Volume not found: " + path.VolumeId);
            }

            return volume;
        }

        public bool Copy(GlobalPath sourcePath, GlobalPath destinationPath, bool verifyFreeSpace = true)
        {
            return Copy(GetVolumeFromPath(sourcePath), sourcePath, GetVolumeFromPath(destinationPath), destinationPath,
                verifyFreeSpace);
        }

        /// <summary>
        /// Copy between resolved volumes. The paths' volume ids are not looked up again.
        /// </summary>
        public bool Copy(Volume sourceVolume, GlobalPath sourcePath, Volume destinationVolume, GlobalPath destinationPath,
            bool verifyFreeSpace = true)
        {
            VolumeItem source = sourceVolume.Open(sourcePath);
            VolumeItem destination = destinationVolume.Open(destinationPath);

            if (source == null)
            {
                throw new KOSPersistenceException("Path does not exist: " + sourcePath);
            }

            if (source is VolumeDirectory)
            {
                if (destination is VolumeFile)
                {
                    throw new KOSPersistenceException("Can't copy directory into a file");
                }

                if (destination == null)
                {
                    destination = destinationVolume.CreateDirectory(destinationPath);
                }
                else if (!sourcePath.IsRoot)
                {
                    destinationPath = destinationPath.Combine(sourcePath.Name);
                    destination = destinationVolume.OpenOrCreateDirectory(destinationPath);
                }

                if (destination == null)
                {
                    throw new KOSException("Path was expected to point to a directory: " + destinationPath);
                }

                return CopyDirectory(sourceVolume, sourcePath, destinationVolume, destinationPath, verifyFreeSpace);
            } else
            {
                if (destination is VolumeFile || destination == null)
                {
                    return CopyFile(source as VolumeFile, destinationPath, destinationVolume, verifyFreeSpace);
                }
                else
                {
                    return CopyFileToDirectory(source as VolumeFile, destination as VolumeDirectory, verifyFreeSpace);
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="destinationPath"/> is inside <paramref name="sourcePath"/> on the
        /// same volume, compared by volume rather than by the paths' volume ids.
        /// </summary>
        private static bool IsInside(Volume sourceVolume, GlobalPath sourcePath, Volume destinationVolume, GlobalPath destinationPath)
        {
            return sourceVolume == destinationVolume && ((VolumePath)sourcePath).IsParent(destinationPath);
        }

        protected bool CopyDirectory(GlobalPath sourcePath, GlobalPath destinationPath, bool verifyFreeSpace)
        {
            return CopyDirectory(GetVolumeFromPath(sourcePath), sourcePath, GetVolumeFromPath(destinationPath),
                destinationPath, verifyFreeSpace);
        }

        protected bool CopyDirectory(Volume sourceVolume, GlobalPath sourcePath, Volume destinationVolume,
            GlobalPath destinationPath, bool verifyFreeSpace)
        {
            if (IsInside(sourceVolume, sourcePath, destinationVolume, destinationPath))
            {
                throw new KOSPersistenceException("Can't copy directory to a subdirectory of itself: " + destinationPath);
            }

            VolumeDirectory source = sourceVolume.Open(sourcePath) as VolumeDirectory;

            VolumeItem destinationItem = destinationVolume.Open(destinationPath);

            if (destinationItem is VolumeFile)
            {
                throw new KOSPersistenceException("Can't copy directory into a file");
            }

            VolumeDirectory destination = destinationItem as VolumeDirectory;

            if (destination == null)
            {
                destination = destinationVolume.CreateDirectory(destinationPath);
            }

            var l = source.List();

            foreach (KeyValuePair<string, VolumeItem> pair in l)
            {
                if (pair.Value is VolumeDirectory)
                {
                    if (!CopyDirectory(sourceVolume, sourcePath.Combine(pair.Key), destinationVolume,
                        destinationPath.Combine(pair.Key), verifyFreeSpace))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!CopyFileToDirectory(pair.Value as VolumeFile, destination, verifyFreeSpace))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        protected bool CopyFile(VolumeFile volumeFile, GlobalPath destinationPath, Volume targetVolume,
            bool verifyFreeSpace)
        {
            return targetVolume.SaveFileRaw(destinationPath, volumeFile.ReadAllRaw(), verifyFreeSpace) != null;
        }

        protected bool CopyFileToDirectory(VolumeFile volumeFile, VolumeDirectory volumeDirectory,
            bool verifyFreeSpace)
        {
            return volumeDirectory.Volume.SaveFileRaw(volumeDirectory.Path.Combine(volumeFile.Name), volumeFile.ReadAllRaw(),
                verifyFreeSpace) != null;
        }

        public bool Move(GlobalPath sourcePath, GlobalPath destinationPath)
        {
            return Move(GetVolumeFromPath(sourcePath), sourcePath, GetVolumeFromPath(destinationPath), destinationPath);
        }

        /// <summary>
        /// Move between resolved volumes. The paths' volume ids are not looked up again.
        /// </summary>
        public bool Move(Volume sourceVolume, GlobalPath sourcePath, Volume destinationVolume, GlobalPath destinationPath)
        {
            if (sourcePath.IsRoot)
            {
                throw new KOSPersistenceException("Can't move root directory: " + sourcePath);
            }

            if (IsInside(sourceVolume, sourcePath, destinationVolume, destinationPath))
            {
                throw new KOSPersistenceException("Can't move directory to a subdirectory of itself: " + destinationPath);
            }

            CheckNotMovedOntoItself(sourceVolume, sourcePath, destinationVolume, destinationPath);

            // A source that cannot be deleted is refused before it is copied:
            var sourceArchive = sourceVolume as Archive;
            if (sourceArchive != null)
                sourceArchive.CheckDeletable(sourcePath);

            bool verifyFreeSpace = sourceVolume != destinationVolume;

            if (!Copy(sourceVolume, sourcePath, destinationVolume, destinationPath, verifyFreeSpace))
            {
                return false;
            }

            if (!sourceVolume.Delete(sourcePath))
            {
                throw new KOSPersistenceException("Can't remove: " + sourcePath);
            }

            return true;
        }

        /// <summary>
        /// A move copies, then deletes the source: refuse one whose copy would land on the source
        /// itself, or for a directory inside it, or the delete removes the only copy. Upstream
        /// compared only the given paths, missing a file moved into its own directory, a volume
        /// alias, a default extension ("p" is p.ks) and another case on a case-insensitive volume.
        /// The destination is found as <see cref="Copy(Volume, GlobalPath, Volume, GlobalPath, bool)"/>
        /// finds it.
        /// </summary>
        private static void CheckNotMovedOntoItself(Volume sourceVolume, GlobalPath sourcePath, Volume destinationVolume,
            GlobalPath destinationPath)
        {
            if (sourceVolume != destinationVolume)
                return;
            VolumeItem source = sourceVolume.Open(sourcePath);
            if (source == null)
                return; // Copy reports it
            VolumeItem destination = destinationVolume.Open(destinationPath);

            VolumePath target;
            if (source is VolumeDirectory)
                target = destination == null ? destinationPath : destinationPath.Combine(sourcePath.Name);
            else
                target = destination is VolumeDirectory ? destination.Path.Combine(source.Name) : destinationPath;

            if (NameSameEntry(sourceVolume, source.Path, target))
                throw new KOSPersistenceException("Can't move onto itself: " + destinationPath);

            if (source is VolumeDirectory)
            {
                VolumePath prefix = target;
                while (prefix.Depth > source.Path.Depth)
                    prefix = prefix.GetParent();
                if (target.Depth > source.Path.Depth && NameSameEntry(sourceVolume, source.Path, prefix))
                    throw new KOSPersistenceException("Can't move directory to a subdirectory of itself: " + destinationPath);
            }
        }

        /// <summary>
        /// Whether two paths on <paramref name="volume"/> name the same file or directory.
        /// Paths differing only in case do on a disk (it ignores case); on the archive it depends
        /// on the file system (<see cref="Archive.NameSameEntryIgnoringCase"/>).
        /// </summary>
        private static bool NameSameEntry(Volume volume, VolumePath a, VolumePath b)
        {
            if (a.Depth != b.Depth)
                return false;
            bool exact = true;
            for (int i = 0; i < a.Segments.Count; ++i)
            {
                if (string.Equals(a.Segments[i], b.Segments[i], StringComparison.Ordinal))
                    continue;
                if (!string.Equals(a.Segments[i], b.Segments[i], StringComparison.InvariantCultureIgnoreCase))
                    return false;
                exact = false;
            }
            if (exact)
                return true;
            var archive = volume as Archive;
            return archive == null || archive.NameSameEntryIgnoringCase(a, b);
        }

        // check the range on the current volume without calling GetVolumeWithRangeCheck
        public virtual bool CheckCurrentVolumeRange()
        {
            return true;
        }

        public virtual bool CheckRange(Volume volume)
        {
            return true;
        }
    }
}
