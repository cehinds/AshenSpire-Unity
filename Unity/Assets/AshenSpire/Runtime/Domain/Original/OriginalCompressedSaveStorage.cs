// Transparent storage encoding for Web's 1 MiB PlayerPrefs budget. Journal
// envelopes, checksums and keys remain unchanged. Existing plain records load
// without migration; later writes encode large records. Older players cannot
// decode the new ASZ1 records. Native platforms keep their existing storage.
using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace AshenSpire.Domain.Original
{
    public sealed class OriginalCompressedSaveStorage : IOriginalSaveStorage
    {
        private const string Prefix = "ASZ1:";
        private const int MaximumDecodedBytes = 32 * 1024 * 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly IOriginalSaveStorage _inner;
        public OriginalCompressedSaveStorage(IOriginalSaveStorage inner)
        { _inner = inner ?? throw new ArgumentNullException(nameof(inner)); }

        public string Read(string key) => Decode(_inner.Read(key));
        private static string Decode(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal)) return value;
            try
            {
                using (var source = new MemoryStream(Convert.FromBase64String(value.Substring(Prefix.Length))))
                using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                using (var decoded = new MemoryStream())
                {
                    var buffer = new byte[8192]; int count;
                    while ((count = gzip.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        if (decoded.Length > MaximumDecodedBytes - count) return value;
                        decoded.Write(buffer, 0, count);
                    }
                    return Utf8.GetString(decoded.ToArray());
                }
            }
            // Leave malformed encoded bytes available to the journal. It rejects
            // them, tries its backup and can quarantine the original value.
            catch (InvalidDataException) { return value; }
            catch (IOException) { return value; }
            catch (FormatException) { return value; }
            catch (ArgumentException) { return value; }
        }

        public void Write(string key, string value) => _inner.Write(key, Encode(value));
        private static string Encode(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 1024) return value;
            var bytes = Utf8.GetBytes(value);
            if (bytes.Length > MaximumDecodedBytes) throw new InvalidDataException("Native save record exceeds the supported decoded size.");
            string encoded;
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true)) gzip.Write(bytes, 0, bytes.Length);
                encoded = Prefix + Convert.ToBase64String(output.ToArray());
            }
            return encoded.Length < value.Length ? encoded : value;
        }
        // Compact historical read-only keys without removing their data. A verified
        // encoded recovery copy precedes replacement; interrupted rewrites can be
        // resumed. The caller validates complete journal envelopes before mutation.
        public bool TryCompact(string key, Action<string> validate)
        {
            if (validate == null) throw new ArgumentNullException(nameof(validate));
            var recoveryKey = key + ".encoding-backup";
            bool Valid(string value)
            {
                if (string.IsNullOrEmpty(value)) return false;
                try { validate(Decode(value)); return true; }
                catch (Exception error) when (!(error is OutOfMemoryException)) { return false; }
            }
            try
            {
                var previous = _inner.Read(key); var pending = _inner.Read(recoveryKey);
                if (!string.IsNullOrEmpty(pending))
                {
                    if (Valid(pending))
                    {
                        // A different valid value may have been written by another
                        // player/tab. Preserve both instead of guessing which wins.
                        if (Valid(previous) && Decode(previous) != Decode(pending)) return false;
                        _inner.Write(key, pending); _inner.Flush();
                        if (_inner.Read(key) != pending) return false;
                        previous = pending;
                    }
                    else if (!Valid(previous)) return false;
                    _inner.Delete(recoveryKey); _inner.Flush();
                }
                if (string.IsNullOrEmpty(previous) || previous.StartsWith(Prefix, StringComparison.Ordinal)) return true;
                validate(previous);
                var compact = Encode(previous); if (compact == previous) return true;
                _inner.Write(recoveryKey, compact); _inner.Flush();
                if (_inner.Read(recoveryKey) != compact) return false;
                _inner.Write(key, compact); _inner.Flush();
                if (_inner.Read(key) != compact) return false;
                _inner.Delete(recoveryKey); _inner.Flush(); return true;
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { return false; }
        }
        public void Delete(string key) => _inner.Delete(key);
        public void Flush() => _inner.Flush();
    }
}
