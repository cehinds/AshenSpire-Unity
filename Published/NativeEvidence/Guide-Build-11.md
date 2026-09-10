# Painted enemies — Unity 0.0.11.0, build 11

Native solo and co-op now use all 19 painted enemies: the 12 earlier sprites you preferred plus 7 new creatures. PNG masters and exact built-in image-generation prompts are preserved. Runtime registration aligns visible silhouettes and feet without changing source pixels.

The current player was checked through real enemy selection, attack feedback, reload and two-player combat/rejoin. Current receipts are in validation.json. Full original-gameplay acceptance from build 10 is linked separately; it is not restamped as build 11 evidence.

Download and unzip Web.zip, then serve it over HTTP. The portable Windows companion includes local hosting instructions and its runtime. Windows.zip and Android.apk are also built from the same source. Keep host keys/private state outside the Web folder.

Edit GameContent/Unity/Original/enemy-art.json and the PNG masters under GameContent/Unity/Art/EnemyExpansion. tools/import-painted-enemies.py copies unchanged PNGs and recalculates alpha registration. The Unity import validates every enemy mapping and caps new textures at 512 pixels. See the owner guide for other content/component editing.

Foundation remains in progress. Original full-map presentation, co-op animation, broader polish, physical-device checks, JS-save import and human balance testing remain. 0.1.0.0 is foundation acceptance; 1.0.0.0 is the completed game.
