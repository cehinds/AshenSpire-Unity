// Document hiding is an interruption; clicking outside the canvas is not.
// Keep handlers on this player Module so disabling/re-enabling cannot leak listeners.
mergeInto(LibraryManager.library, {
  AshenSpire_WatchVisibility: function(ownerPointer) {
    if (Module.ashenSpireVisibilityCleanup) Module.ashenSpireVisibilityCleanup();
    var owner = UTF8ToString(ownerPointer);
    var pageHidden = false;
    var report = function() {
      Module.SendMessage(owner, 'OnBrowserVisibilityChanged', document.hidden || pageHidden ? 1 : 0);
    };
    var hide = function() { pageHidden = true; report(); };
    var show = function() { pageHidden = false; report(); };
    document.addEventListener('visibilitychange', report);
    window.addEventListener('pagehide', hide);
    window.addEventListener('pageshow', show);
    Module.ashenSpireVisibilityCleanup = function() {
      document.removeEventListener('visibilitychange', report);
      window.removeEventListener('pagehide', hide);
      window.removeEventListener('pageshow', show);
      Module.ashenSpireVisibilityCleanup = null;
    };
    report();
  },
  AshenSpire_UnwatchVisibility: function() {
    if (Module.ashenSpireVisibilityCleanup) Module.ashenSpireVisibilityCleanup();
  }
});
