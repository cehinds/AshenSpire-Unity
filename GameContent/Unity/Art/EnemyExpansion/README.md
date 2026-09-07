# Painted enemies for the native climb

The owner preferred the twelve painted enemies generated earlier for the Unity
adaptation. Native solo and cooperative combat now use that same set, extended
with seven newly generated creatures: Husk Brute, Stitched Hound, Court
Marionette, Ash Revenant, Ember-Starved Pilgrim, Charred Colossus and Blighted
Valkyrie. The original procedural models remain preserved as source references.

Each new creature was generated individually with the built-in image-generation
tool. Exact prompts and generated-file provenance are beside the PNGs. They are
transparent RGBA originals; no pixel editing or re-encoding was applied.

Run `tools/import-painted-enemies.py` with Python and Pillow to copy the source
PNGs unchanged into Resources and record their visible alpha bounds. The
`GameContent/Unity/Original/enemy-art.json` catalog maps all 19 stable enemy IDs
to Resources paths and normalized bounds. `OriginalEnemyFigure` fits each visible
silhouette into the same frame and places its feet on the 94% baseline. This
corrects inconsistent generated margins during rendering. Combat feedback
continues to own the single movement/tint timeline.

Unity import caps new enemy textures at 512 pixels, preserves alpha and disables
mipmaps and CPU readback. The high-resolution generation masters remain here.
Content import fails for a missing enemy mapping, PNG or invalid registration.
