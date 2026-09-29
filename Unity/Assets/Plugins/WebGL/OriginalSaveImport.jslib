// Explicit user-selected reads only. Never writes/deletes original-game storage.
mergeInto(LibraryManager.library, {
  AshenedSpire_ChooseOriginalSave: function(ownerPointer) {
    var owner = UTF8ToString(ownerPointer);
    var input = document.createElement('input');
    input.type = 'file'; input.accept = '.json,application/json';
    input.style.display = 'none'; document.body.appendChild(input);
    var report = function(result) { Module.SendMessage(owner, 'OnOriginalSaveRead', JSON.stringify(result)); };
    input.oncancel = function() { input.remove(); };
    input.onchange = function() {
      var file = input.files && input.files[0]; input.remove();
      if (!file) return;
      if (file.size > 1048576) { report({error:'Choose a save file no larger than 1 MB.'}); return; }
      var reader = new FileReader();
      reader.onload = function() { report({save:reader.result}); };
      reader.onerror = function() { report({error:'The save file could not be read. Your saves are unchanged.'}); };
      reader.readAsText(file);
    };
    input.click();
  },
  AshenedSpire_ReadOriginalSlot: function(ownerPointer, slot) {
    var owner = UTF8ToString(ownerPointer);
    var report = function(result) { Module.SendMessage(owner, 'OnOriginalSaveRead', JSON.stringify(result)); };
    if (slot < 0 || slot > 2) { report({error:'Choose an original slot from 1 to 3.'}); return; }
    try {
      var value = window.localStorage.getItem(['sote_run_v1','sote_run_v1_s2','sote_run_v1_s3'][slot]);
      if (!value) { report({error:'No original save in this slot on this website. Use the same browser and website as the original game, or choose an exported save file.'}); return; }
      if (new TextEncoder().encode(value).length > 1048576) { report({error:'This save is larger than the supported 1 MB limit.'}); return; }
      report({save:value});
    } catch (error) { report({error:'This browser could not read the original save. Try choosing an exported file.'}); }
  }
});
