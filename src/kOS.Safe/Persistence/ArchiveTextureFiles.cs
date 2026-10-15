using System.IO;
using kOS.Safe.Exceptions;

namespace kOS.Safe.Persistence
{
    /// <summary>
    /// The files a script GUI image path may load, and the limits on them (IO-04).
    /// </summary>
    public static class ArchiveTextureFiles
    {
        public static readonly string[] Extensions = { ".png", "" };

        /// <summary>Largest image file read: 16 MiB.</summary>
        public const long MaxFileBytes = 16L * 1024 * 1024;
        /// <summary>Largest width or height a PNG or JPEG header may give (64 MiB as RGBA).</summary>
        public const int MaxSide = 4096;

        /// <summary>
        /// The files to try, in order: "name.png", then "name" as given. Both must be inside
        /// the archive; the archive folder itself is no image, and "name" must not be the
        /// folder either, or ".png" would name a file next to it.
        /// </summary>
        public static string[] Candidates(string archiveFolder, string relativePath)
        {
            string path = DirectoryContainment.Combine(archiveFolder, relativePath);
            if (!DirectoryContainment.IsBelow(archiveFolder, path))
                throw new KOSInvalidPathException("Path refers to parent directory", relativePath);

            var files = new string[Extensions.Length];
            for (int i = 0; i < Extensions.Length; ++i)
                files[i] = path + Extensions[i];
            return files;
        }

        /// <summary>
        /// Read an image file, refusing one over <see cref="MaxFileBytes"/> before reading it
        /// and a PNG or JPEG whose header is over <see cref="MaxSide"/> before it is decoded.
        /// Other contents are returned as they are, for the image loader to accept or not.
        /// </summary>
        /// <param name="scriptPath">The path as the script gave it, for messages.</param>
        public static byte[] ReadImage(string file, string scriptPath)
        {
            byte[] bytes;
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long length = stream.Length;
                if (length > MaxFileBytes)
                    throw new KOSException(string.Format(
                        "Image file too large: {0} is {1} bytes, the limit is {2} bytes (16 MiB)", scriptPath, length, MaxFileBytes));
                bytes = new byte[length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int count = stream.Read(bytes, read, bytes.Length - read);
                    if (count == 0)
                        break;
                    read += count;
                }
                if (read < bytes.Length)
                    System.Array.Resize(ref bytes, read);
            }

            int width, height;
            if (TryReadSize(bytes, out width, out height) && (width > MaxSide || height > MaxSide))
                throw new KOSException(string.Format(
                    "Image too large: {0} is {1}x{2} pixels, the limit is {3}x{3}", scriptPath, width, height, MaxSide));
            return bytes;
        }

        /// <summary>The size a PNG or JPEG header gives, without decoding the image.</summary>
        public static bool TryReadSize(byte[] bytes, out int width, out int height)
        {
            width = height = 0;
            if (bytes == null)
                return false;
            return TryReadPngSize(bytes, ref width, ref height) || TryReadJpegSize(bytes, ref width, ref height);
        }

        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private static bool TryReadPngSize(byte[] bytes, ref int width, ref int height)
        {
            // Signature, then the IHDR chunk: length, "IHDR", width, height (big-endian).
            if (bytes.Length < 24)
                return false;
            for (int i = 0; i < PngSignature.Length; ++i)
                if (bytes[i] != PngSignature[i])
                    return false;
            if (bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R')
                return false;
            width = ReadBigEndian32(bytes, 16);
            height = ReadBigEndian32(bytes, 20);
            return true;
        }

        private static bool TryReadJpegSize(byte[] bytes, ref int width, ref int height)
        {
            if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
                return false;
            int i = 2;
            while (i < bytes.Length)
            {
                if (bytes[i] != 0xFF)
                    return false;
                while (i < bytes.Length && bytes[i] == 0xFF) // fill bytes
                    ++i;
                if (i >= bytes.Length)
                    return false;
                byte marker = bytes[i++];
                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD8))
                    continue; // markers without a segment
                if (marker == 0xD9 || marker == 0xDA)
                    return false; // end of image, or image data before any frame header
                if (i + 1 >= bytes.Length)
                    return false;
                int segmentLength = (bytes[i] << 8) | bytes[i + 1];
                bool frameHeader = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (frameHeader)
                {
                    // Length, precision, height, width.
                    if (i + 6 >= bytes.Length)
                        return false;
                    height = (bytes[i + 3] << 8) | bytes[i + 4];
                    width = (bytes[i + 5] << 8) | bytes[i + 6];
                    return true;
                }
                if (segmentLength < 2)
                    return false;
                i += segmentLength;
            }
            return false;
        }

        private static int ReadBigEndian32(byte[] bytes, int offset)
        {
            uint value = ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
            return value > int.MaxValue ? int.MaxValue : (int)value;
        }
    }
}
