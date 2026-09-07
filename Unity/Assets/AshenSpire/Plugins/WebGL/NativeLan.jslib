// NativeLan.jslib — WebSocket byte transport for NativeLanClient.
// This bridge owns no game rules, seat authority, random numbers or card effects.
mergeInto(LibraryManager.library, {
  $AshenSpireLan: { sockets: {} },
  AS_NativeLan_Connect__deps: ['$AshenSpireLan'],
  AS_NativeLan_Connect: function(receiverPtr, urlPtr, generation) {
    var receiver = UTF8ToString(receiverPtr), url = UTF8ToString(urlPtr);
    var old = AshenSpireLan.sockets[receiver];
    if (old) { delete AshenSpireLan.sockets[receiver]; old.socket.close(); }
    var state = { generation: generation, socket: null };
    function emit(type, data) {
      if (AshenSpireLan.sockets[receiver] !== state) return;
      SendMessage(receiver, 'OnNativeLanEvent', JSON.stringify({ generation: generation, type: type, data: data || '' }));
    }
    try {
      state.socket = new WebSocket(url, 'ashenspire.native.v1'); AshenSpireLan.sockets[receiver] = state;
      state.socket.onopen = function() { emit('open', ''); };
      state.socket.onmessage = function(event) {
        if (typeof event.data !== 'string' || new TextEncoder().encode(event.data).length > 2097152) { emit('error', 'message_limit'); state.socket.close(4009, 'message too large'); return; }
        emit('message', event.data);
      };
      state.socket.onerror = function() { emit('error', 'companion_unreachable'); };
      state.socket.onclose = function() { emit('close', 'companion_disconnected'); if (AshenSpireLan.sockets[receiver] === state) delete AshenSpireLan.sockets[receiver]; };
    } catch (error) {
      AshenSpireLan.sockets[receiver] = state; emit('error', 'companion_unreachable'); emit('close', 'companion_disconnected'); delete AshenSpireLan.sockets[receiver];
    }
  },
  AS_NativeLan_Send__deps: ['$AshenSpireLan'],
  AS_NativeLan_Send: function(receiverPtr, generation, messagePtr) {
    var receiver = UTF8ToString(receiverPtr), state = AshenSpireLan.sockets[receiver], message = UTF8ToString(messagePtr);
    if (!state || state.generation !== generation || state.socket.readyState !== WebSocket.OPEN) return;
    if (new TextEncoder().encode(message).length > 16384 || state.socket.bufferedAmount > 262144) { state.socket.close(4009, 'outgoing limit'); return; }
    state.socket.send(message);
  },
  AS_NativeLan_Close__deps: ['$AshenSpireLan'],
  AS_NativeLan_Close: function(receiverPtr) {
    var receiver = UTF8ToString(receiverPtr), state = AshenSpireLan.sockets[receiver];
    if (!state) return; delete AshenSpireLan.sockets[receiver]; if (state.socket) state.socket.close();
  }
});


