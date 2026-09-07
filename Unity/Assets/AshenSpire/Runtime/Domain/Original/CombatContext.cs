// CombatContext.cs — plain combat state dependencies and a bounded FIFO action queue.
// Controllers submit commands; effects mutate through registered handlers; observers
// receive event copies. Rendering and animation never execute a queued action.
// Register capabilities explicitly. Unsupported effects fail before a batch is queued.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AshenSpire.Domain.Original
{
    public sealed class CombatAction
    {
        public JObject Effect { get; }
        public JObject Source { get; }
        public JObject Owner { get; }
        public JObject Target { get; }
        public CombatAction(JObject effect, JObject source, JObject owner, JObject target)
        { Effect = (JObject)effect.DeepClone(); Source = source; Owner = owner; Target = target; }
    }
    public sealed class CombatContext
    {
        private readonly Queue<CombatAction> _queue = new Queue<CombatAction>();
        private readonly List<JObject> _events = new List<JObject>();
        private readonly Dictionary<string, Action<CombatAction>> _handlers = new Dictionary<string, Action<CombatAction>>(StringComparer.Ordinal);
        private bool _draining;
        public OriginalContentCatalog Content { get; }
        public JObject Player { get; }
        public IReadOnlyList<JObject> Enemies { get; }
        public int PendingCount => _queue.Count;
        public event Action<JObject> Emitted;
        public CombatContext(OriginalContentCatalog content, JObject player, IReadOnlyList<JObject> enemies)
        { Content = content; Player = player; Enemies = enemies; }
        public void Emit(string type, JObject payload)
        {
            var record = new JObject { ["type"] = type };
            foreach (var property in payload.Properties()) record[property.Name] = property.Value.DeepClone();
            _events.Add(record); Emitted?.Invoke((JObject)record.DeepClone());
        }
        public JArray Events()
        { var result = new JArray(); foreach (var record in _events) result.Add(record.DeepClone()); return result; }
        public void Enqueue(CombatAction action)
        {
            if (_queue.Count >= 10000) throw new InvalidOperationException("Combat queue capacity exceeded.");
            _queue.Enqueue(action);
        }
        public void Register(string operation, Action<CombatAction> handler)
        {
            if (string.IsNullOrEmpty(operation) || handler == null || _handlers.ContainsKey(operation)) throw new ArgumentException("Invalid or duplicate combat handler: " + operation);
            _handlers.Add(operation, handler);
        }
        public void Submit(IReadOnlyList<CombatAction> actions)
        {
            foreach (var action in actions) if (!_handlers.ContainsKey((string)action.Effect["op"])) throw new NotSupportedException("Combat operation has not been ported: " + action.Effect["op"]);
            foreach (var action in actions) Enqueue(action);
            Drain();
        }
        public void Drain()
        {
            if (_draining) return;
            _draining = true;
            try
            {
                var count = 0;
                while (_queue.Count > 0)
                {
                    if (++count > 10000) throw new InvalidOperationException("Combat action queue did not drain.");
                    var action = _queue.Peek();
                    var op = (string)action.Effect["op"];
                    if (!_handlers.TryGetValue(op, out var handler)) throw new NotSupportedException("Combat operation has not been ported: " + op);
                    _queue.Dequeue(); handler(action);
                }
            }
            finally { _draining = false; }
        }
        public JArray PendingEffects()
        { var result = new JArray(); foreach (var action in _queue) result.Add(action.Effect.DeepClone()); return result; }
    }
}
