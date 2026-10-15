using kOS.Safe.Exceptions;

namespace kOS.Safe.Persistence
{
    public static class VolumeArguments
    {
        /// <summary>
        /// The volume a path argument names. A Volume or VolumeItem names its own volume:
        /// its path's volume id is only a name, and two volumes can share one (IO-05). It
        /// must still be one of the manager's volumes and in range. A path without a volume
        /// id ("x.ks", "/x.ks") is on the current volume, whose name may also be shared.
        /// Anything else (a string or path with a volume id) is looked up by
        /// <paramref name="path"/>, as before.
        /// </summary>
        /// <param name="pathObject">The argument given to <see cref="IVolumeManager.GlobalPathFromObject"/>.</param>
        /// <param name="path">The path <see cref="IVolumeManager.GlobalPathFromObject"/> made of it.</param>
        public static Volume GetVolumeFromObject(this IVolumeManager manager, object pathObject, GlobalPath path)
        {
            Volume volume = pathObject as Volume;
            VolumeItem item = pathObject as VolumeItem;
            if (item != null)
                volume = item.Volume;
            if (volume == null)
            {
                // Same test as VolumeManager.GlobalPathFromString.
                if (pathObject != null && !GlobalPath.HasVolumeId(pathObject.ToString()))
                    return manager.CurrentVolume;
                return manager.GetVolumeFromPath(path);
            }

            if (manager.GetVolumeId(volume) < 0)
                throw new KOSPersistenceException("Volume not found: \"" + volume.Name + "\" is not attached to this processor");
            if (!manager.CheckRange(volume))
                throw new KOSVolumeOutOfRangeException();
            return volume;
        }
    }
}
