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
        private sealed class PendingAction
        {
            internal CombatAction Action;
            internal CombatContext Executor;
        }
        private sealed class ActionQueue
        {
            internal readonly Queue<PendingAction> Items = new Queue<PendingAction>();
            internal bool Draining;
        }
        private ActionQueue _queue = new ActionQueue();
        private Action<Action> _executionScope;
        private readonly List<JObject> _events = new List<JObject>();
        private readonly Dictionary<string, Action<CombatAction>> _handlers = new Dictionary<string, Action<CombatAction>>(StringComparer.Ordinal);
        public OriginalContentCatalog Content { get; }
        public JObject Player { get; }
        public IReadOnlyList<JObject> Enemies { get; }
        public int PendingCount => _queue.Items.Count;
        // Current party rules use one bounded FIFO. Each entry retains the seat
        // interpreter that queued it, including that seat's piles and metadata.
        internal void ShareQueue(CombatContext first, Action<Action> executionScope)
        {
            if (_queue.Draining || _queue.Items.Count != 0) throw new InvalidOperationException("Cannot replace a live combat queue.");
            if (first != null) _queue = first._queue;
            _executionScope = executionScope ?? throw new ArgumentNullException(nameof(executionScope));
        }
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
            if (_queue.Items.Count >= 10000) throw new InvalidOperationException("Combat queue capacity exceeded.");
            _queue.Items.Enqueue(new PendingAction { Action = action, Executor = _executionScope == null ? null : this });
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
            if (_queue.Draining) return;
            _queue.Draining = true;
            try
            {
                var count = 0;
                while (_queue.Items.Count > 0)
                {
                    if (++count > 10000) throw new InvalidOperationException("Combat action queue did not drain.");
                    var pending = _queue.Items.Peek();
                    var action = pending.Action;
                    var executor = pending.Executor ?? this;
                    var op = (string)action.Effect["op"];
                    if (!executor._handlers.TryGetValue(op, out var handler)) throw new NotSupportedException("Combat operation has not been ported: " + op);
                    _queue.Items.Dequeue();
                    if (executor._executionScope == null) handler(action);
                    else executor._executionScope(() => handler(action));
                }
            }
            finally { _queue.Draining = false; }
        }
        internal void TransferPendingTo(CombatContext destination)
        { if (ReferenceEquals(_queue,destination._queue)) return; if (_queue.Draining) throw new InvalidOperationException("Cannot transfer a draining queue."); while (_queue.Items.Count > 0) destination.Enqueue(_queue.Items.Dequeue().Action); }
        public JArray PendingEffects()
        { var result = new JArray(); foreach (var pending in _queue.Items) result.Add(pending.Action.Effect.DeepClone()); return result; }
    }
}
