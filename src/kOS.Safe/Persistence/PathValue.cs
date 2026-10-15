using System;
using kOS.Safe.Serialization;
using kOS.Safe.Persistence;
using kOS.Safe.Encapsulation.Suffixes;
using kOS.Safe.Encapsulation;
using System.Linq;
using kOS.Safe.Function;

namespace kOS.Safe
{
    /// <summary>
    /// Contains suffixes related to GlobalPath.
    ///
    /// This exists as a separate class because some of the suffixes require an instance of VolumeManager to work. I think
    /// it would be counter-productive to pass around an instance of VolumeManager whenever we're dealing with GlobalPath internally.
    /// Instances of this class are on the other hand created only for the user.
    /// </summary>
    [kOS.Safe.Utilities.KOSNomenclature("Path")]
    public class PathValue : SerializableStructure
    {
        [Function("path")]
        public class FunctionPath : SafeFunctionBase
        {
            public override void Execute(SafeSharedObjects shared)
            {
                int remaining = CountRemainingArgs(shared);

                GlobalPath path;
                Volume ownVolume;

                if (remaining == 0)
                {
                    ownVolume = shared.VolumeMgr.CurrentVolume;
                    path = GlobalPath.FromVolumePath(shared.VolumeMgr.CurrentDirectory.Path,
                                                     shared.VolumeMgr.GetVolumeRawIdentifier(ownVolume));
                }
                else
                {
                    object pathObject = PopValueAssert(shared, true);
                    path = shared.VolumeMgr.GlobalPathFromObject(pathObject);
                    // Keep the volume the argument names, as path functions use it (PATH-01): a
                    // path without a volume id is on the current volume.
                    ownVolume = VolumeArguments.OwnVolume(pathObject);
                    if (ownVolume == null && !GlobalPath.HasVolumeId(pathObject.ToString()))
                        ownVolume = shared.VolumeMgr.CurrentVolume;
                }

                AssertArgBottomAndConsume(shared);

                ReturnValue = new PathValue(path, shared, ownVolume);
            }
        }

        private const string DumpPath = "path";

        public GlobalPath Path { get; private set; }
        private SafeSharedObjects sharedObjects;

        /// <summary>
        /// The volume this path is on, when it was made from one (the current volume, a volume
        /// or file object, or a path that had one) rather than from a "name:" string; null
        /// otherwise, and after loading from a dump. Two volumes can share a name, so
        /// <see cref="Path"/>'s volume id alone may find the other one (PATH-01). Printing,
        /// saving and comparing still use <see cref="Path"/> only.
        /// </summary>
        public Volume OwnVolume { get; private set; }

        public SafeSharedObjects Shared {
            set
            {
                sharedObjects = value;
            }
        }

        // Only used by CreateFromDump() and other peer constructors.
        // Don't make it public because it leaves fields
        // unpopulated:
        private PathValue()
        {
            InitializeSuffixes();
        }

        public PathValue(GlobalPath path, SafeSharedObjects sharedObjects) : this(path, sharedObjects, null)
        {
        }

        public PathValue(GlobalPath path, SafeSharedObjects sharedObjects, Volume ownVolume) : this()
        {
            Path = path;
            this.sharedObjects = sharedObjects;
            OwnVolume = ownVolume;
        }

        // Required for all IDumpers for them to work, but can't enforced by the interface because it's static:
        public static PathValue CreateFromDump(SafeSharedObjects shared, Dump d)
        {
            var newObj = new PathValue();
            newObj.Shared = shared;
            newObj.LoadDump(d);
            return newObj;
        }

        public PathValue FromPath(GlobalPath path)
        {
            return new PathValue(path, sharedObjects, OwnVolume);
        }

        public PathValue FromPath(VolumePath volumePath, string volumeId)
        {
            return new PathValue(GlobalPath.FromVolumePath(volumePath, volumeId), sharedObjects, OwnVolume);
        }

        private Volume GetVolume()
        {
            if (OwnVolume != null)
                return sharedObjects.VolumeMgr.CheckAttachedAndInRange(OwnVolume);
            return sharedObjects.VolumeMgr.GetVolumeFromPath(Path);
        }

        private void InitializeSuffixes()
        {
            AddSuffix("VOLUME", new Suffix<Volume>(GetVolume));
            AddSuffix("SEGMENTS", new Suffix<ListValue>(() => new ListValue(Path.Segments.Select((s) => (Structure)new StringValue(s)))));
            AddSuffix("LENGTH", new Suffix<ScalarIntValue>(() => Path.Length));
            AddSuffix("NAME", new Suffix<StringValue>(() => Path.Name));
            AddSuffix("HASEXTENSION", new Suffix<BooleanValue>(() => string.IsNullOrEmpty(Path.Extension)));
            AddSuffix("EXTENSION", new Suffix<StringValue>(() => Path.Extension));
            AddSuffix("ROOT", new Suffix<PathValue>(() => FromPath(Path.RootPath())));
            AddSuffix("PARENT", new Suffix<PathValue>(() => FromPath(Path.GetParent())));

            AddSuffix("ISPARENT", new OneArgsSuffix<BooleanValue, PathValue>((p) => Path.IsParent(p.Path)));
            AddSuffix("CHANGENAME", new OneArgsSuffix<PathValue, StringValue>((n) => FromPath(Path.ChangeName(n))));
            AddSuffix("CHANGEEXTENSION", new OneArgsSuffix<PathValue, StringValue>((e) => FromPath(Path.ChangeExtension(e))));
            AddSuffix("COMBINE", new VarArgsSuffix<PathValue, Structure>(Combine));
        }
        public PathValue Combine(params Structure[] segments)
        {
            if (segments.All(s => s.GetType() == typeof(StringValue)))
            {
                return Combine(segments.Cast<StringValue>().ToArray());
            }
            throw new Exceptions.KOSInvalidArgumentException("PATH:COMBINE", "SEGMENTS", "all segments must be strings");
        }

        public PathValue Combine(params StringValue[] segments)
        {
            return FromPath(Path.Combine(segments.Select(s => s.ToString()).ToArray()));
        }

        public override Dump Dump()
        {
            return new Dump { { DumpPath, Path.ToString() } };
        }

        public override void LoadDump(Dump dump)
        {
            Path = GlobalPath.FromString(dump[DumpPath] as string);
        }

        public override string ToString()
        {
            return Path.ToString();
        }

        public override bool Equals(object other)
        {
            PathValue pVal = other as PathValue;
            if (!ReferenceEquals(pVal,null)) // ReferenceEquals prevents infinite recursion with overloaded == operator.
                return Path == pVal.Path;
            GlobalPath gVal = other as GlobalPath;
            if (!ReferenceEquals(gVal,null)) // ReferenceEquals prevents infinite recursion with overloaded == operator.
                return Path == gVal;

            // fallback:
            return base.Equals(other);
        }

        public override int GetHashCode()
        {
            if (!ReferenceEquals(Path,null)) // ReferenceEquals prevents infinite recursion with overloaded == operator.
                return Path.GetHashCode();
            return base.GetHashCode();
        }

        public static bool operator ==(PathValue left, PathValue right)
        {
            if (ReferenceEquals(left,null) || ReferenceEquals(right,null)) // ReferenceEquals prevents infinite recursion with overloaded == operator.
                return ReferenceEquals(left, null) && ReferenceEquals(right, null); // ReferenceEquals prevents infinite recursion with overloaded == operator.
            return left.Equals(right);
        }
        public static bool operator !=(PathValue left, PathValue right)
        {
            return !(left == right);
        }

    }
}

