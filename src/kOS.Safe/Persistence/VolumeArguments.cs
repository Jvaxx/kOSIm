using kOS.Safe.Exceptions;

namespace kOS.Safe.Persistence
{
    public static class VolumeArguments
    {
        /// <summary>
        /// The volume a path argument names. A Volume, a VolumeItem or a PATH value that kept
        /// its volume names its own volume: its path's volume id is only a name, and two
        /// volumes can share one (IO-05, PATH-01). It
        /// must still be one of the manager's volumes and in range. A path without a volume
        /// id ("x.ks", "/x.ks") is on the current volume, whose name may also be shared.
        /// Anything else (a string or path with a volume id) is looked up by
        /// <paramref name="path"/>, as before.
        /// </summary>
        /// <param name="pathObject">The argument given to <see cref="IVolumeManager.GlobalPathFromObject"/>.</param>
        /// <param name="path">The path <see cref="IVolumeManager.GlobalPathFromObject"/> made of it.</param>
        public static Volume GetVolumeFromObject(this IVolumeManager manager, object pathObject, GlobalPath path)
        {
            Volume volume = OwnVolume(pathObject);
            if (volume == null)
            {
                // Same test as VolumeManager.GlobalPathFromString.
                if (pathObject != null && !GlobalPath.HasVolumeId(pathObject.ToString()))
                    return manager.CurrentVolume;
                return manager.GetVolumeFromPath(path);
            }

            return CheckAttachedAndInRange(manager, volume);
        }

        /// <summary>
        /// The volume an object names by itself rather than by its path's volume id: a
        /// Volume, a VolumeItem's volume, or the volume a PATH value kept (PATH-01); null for
        /// anything else.
        /// </summary>
        public static Volume OwnVolume(object pathObject)
        {
            Volume volume = pathObject as Volume;
            if (volume != null)
                return volume;
            VolumeItem item = pathObject as VolumeItem;
            if (item != null)
                return item.Volume;
            PathValue pathValue = pathObject as PathValue;
            return pathValue == null ? null : pathValue.OwnVolume;
        }

        /// <summary>
        /// <paramref name="volume"/> itself, if it is one of the manager's volumes and in range;
        /// never another volume of the same name.
        /// </summary>
        public static Volume CheckAttachedAndInRange(this IVolumeManager manager, Volume volume)
        {
            if (manager.GetVolumeId(volume) < 0)
                throw new KOSPersistenceException("Volume not found: \"" + volume.Name + "\" is not attached to this processor");
            if (!manager.CheckRange(volume))
                throw new KOSVolumeOutOfRangeException();
            return volume;
        }
    }
}
