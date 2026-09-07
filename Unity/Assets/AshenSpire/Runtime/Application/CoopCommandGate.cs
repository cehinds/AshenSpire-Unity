// CoopCommandGate.cs — pure client command acknowledgement gate; no game rules.
// A peer broadcast cannot finish our action. Both the matching successful receipt
// and a view carrying our committed sequence are required, in either arrival order.
// Reset when disconnecting; rejoin obtains its next sequence from host state.
using System;
namespace AshenSpire.Application
{
    public sealed class CoopCommandGate
    {
        private string _requestId;
        private long _sequence, _observed;
        private bool _accepted;
        public bool IsPending => _requestId != null;
        public void Begin(string requestId, long sequence)
        {
            if (IsPending) throw new InvalidOperationException("A command is already pending.");
            if (string.IsNullOrEmpty(requestId) || sequence < 1) throw new ArgumentException("A request ID and positive sequence are required.");
            _requestId = requestId; _sequence = sequence; _observed = 0; _accepted = false;
        }
        public void Observe(long sequence) { if (!IsPending) return; _observed = Math.Max(_observed, sequence); Complete(); }
        public void Accept(string requestId) { if (_requestId == null || requestId != _requestId) return; _accepted = true; Complete(); }
        public void Reject(string requestId) { if (_requestId != null && requestId == _requestId) Reset(); }
        private void Complete() { if (_accepted && _observed >= _sequence) Reset(); }
        public void Reset() { _requestId = null; _sequence = _observed = 0; _accepted = false; }
    }
}
