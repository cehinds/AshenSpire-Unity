// OriginalSaveStorage.cs — the one string key/value boundary every native save uses.
// Unity plugs in PlayerPrefs (or a file store); tests plug in the memory store below.
// Read returns null or "" for a missing key. Nothing here knows the save format.
using System;
using System.Collections.Generic;
namespace AshenSpire.Domain.Original
{
    public interface IOriginalSaveStorage
    {
        string Read(string key);
        void Write(string key, string value);
        void Delete(string key);
        void Flush();
    }
    // Adapts the delegate trio OriginalSaveJournal always accepted. Without a delete
    // delegate a removal writes "", which every reader already treats as missing.
    public sealed class OriginalDelegateSaveStorage : IOriginalSaveStorage
    {
        private readonly Func<string, string> _read;
        private readonly Action<string, string> _write;
        private readonly Action<string> _delete;
        private readonly Action _flush;
        public OriginalDelegateSaveStorage(Func<string, string> read, Action<string, string> write, Action flush, Action<string> delete = null)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read)); _write = write ?? throw new ArgumentNullException(nameof(write));
            _flush = flush ?? (() => { }); _delete = delete;
        }
        public string Read(string key) => _read(key);
        public void Write(string key, string value) => _write(key, value);
        public void Delete(string key) { if (_delete != null) _delete(key); else _write(key, ""); }
        public void Flush() => _flush();
    }
    // Deterministic in-memory store (ordinal key order) for tests and headless tools.
    public sealed class OriginalMemorySaveStorage : IOriginalSaveStorage
    {
        private readonly SortedDictionary<string, string> _values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public int Flushes { get; private set; }
        public IReadOnlyDictionary<string, string> Values => _values;
        public string Read(string key) => _values.TryGetValue(key, out var value) ? value : null;
        public void Write(string key, string value) => _values[key] = value;
        public void Delete(string key) => _values.Remove(key);
        public void Flush() => Flushes++;
    }
}
