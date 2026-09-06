// Read-only bridge. clientHeight includes CSS layout, independently of render density.
// Use this Unity instance's canvas; do not assume a full-window or unique page canvas.
mergeInto(LibraryManager.library, {
  AshenSpire_CanvasHeight: function () {
    return Module.canvas ? Module.canvas.clientHeight : 0;
  }
});
