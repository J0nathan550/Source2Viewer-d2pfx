# Source2Viewer - d2pfx

Source2Viewer - d2pfx is a modded build of [Source 2 Viewer](https://github.com/ValveResourceFormat/ValveResourceFormat) (ValveResourceFormat), made for the [d2pfx](https://h6rd.github.io/Dota2PornFxWeb/) community.

It adds a custom VMDL extraction option to the package viewer's right-click menu, built on top of [VmdlExtractor](https://github.com/fvckmindself/VmdlExtractor) by [fvckmindself](https://github.com/fvckmindself).

It also adds a custom VMAT extraction option for `.vmat_c` materials. The option writes every texture the material uses next to the `.vmat` and points the `.vmat` at them by their path inside your addon (for example `j0nathan550/materials/razor_arcana_head_color.png`), so the material compiles without any red, missing textures. Cubemaps are stitched into one 4:3 cross `.png`, the layout Dota 2's own cubemap sources use, instead of six separate face images. Each custom option only appears when the selection contains files of its type.

It also adds an "Export character assets (items_game.txt)" option to the right-click menu of Dota 2's `pak01_dir.vpk`. It reads `scripts/items/items_game.txt` and the hero scripts from the package every time it opens, so it always matches the current game. Pick a hero, an item set or the item and style for each loadout slot, and see the result in a live preview. Heroes with a persona switch their whole loadout when the persona is picked. Items whose model comes with several materials, like the golden and prismatic versions of an item, get a skin picker next to them, named after the items that use each material. It starts out as the one the item and its style use.

The export goes into a Dota addon: pick its content folder (e.g. `dota 2 beta/content/dota_addons/<addon>`) and its game folder, which is filled in from the content folder. The content folder gets the models the hero and items wear, the models of the units the hero creates like Beastmaster's boar, the particles they create or swap in, and the hero's `game_sounds` and `game_sounds_vo` sound event files, laid out like the package. Nothing these reference is exported on top of them, since the addon loads materials, textures and other heroes' assets from the game like the game does. The sounds themselves are only exported when you ask for them, otherwise the sound event files keep pointing at the ones in the game. Models go through the custom VMDL extractor, everything else through the built-in decompiler. Pedestals and the particles items only play in the loadout screen are left out unless you tick "Pedestal", which is only available when an equipped item comes with one.

The Icons tab lists the hero's portrait, minimap icon, ability icons and the shop item icons its items change, each with every version the hero's items come with and a thumbnail. They start out as the versions the equipped items and styles show, and can be picked by hand. With "Replace icons" on, the export copies the picked versions into the game folder under the names of the hero's own icons, as the compiled `.vtex_c` images the game has, e.g. `npc_dota_hero_drow_ranger_alt2_png.vtex_c` as `panorama/images/heroes/npc_dota_hero_drow_ranger_png.vtex_c`.

With "Replace default assets" on, the export also writes the chosen look over the hero's default files, so it shows without the items equipped. The arcana or persona model is written as the hero's base model (e.g. `models/heroes/earthshaker/earthshaker.vmdl`) with the chosen style's skin as its default. Chosen items and their refits are written over the default items' models, and items that dress a unit the hero creates are written over that unit's model. Every model the hero wears gets the body group choices the equipped items set, like the "arcana" body group the arcana level switches, and the meshes of the other choices are removed from it. That way a style 2 arcana shows its style 2 meshes, and the parts it hides on other items, like Earthshaker's saddle, stay hidden. Items without a default model to be written over, like a head for a hero without a default head, and the extra models items add, have their meshes added to the hero's base model. Particles the items swap in are written over the ones they replace, and particles the items create are added to the models as model particles. Particles every hero uses, like the blink dagger or stun effects, are only replaced when you also tick "Also shared particles".

# Source2Viewer Avalonia

Before the project was done using Forms and for old users Forms project remains. There is another implementation done with Avalonia.
Avalonia Implementation provides 1:1 recreation of Source2Viewer Forms. Core idea was to make Source2Viewer work natively in Linux.

Avalonia related implementation you can find in this repository in GUI.Avalonia folder.
Below there is provided image of Source2Viewer working on Linux with help of Avalonia.

<img width="1494" height="853" alt="image" src="https://github.com/user-attachments/assets/9d747bcd-a170-4933-a4ed-cbd92f97c11f" />
<img width="1422" height="794" alt="image" src="https://github.com/user-attachments/assets/bef09893-e3bd-416a-9fd6-b8257d6004f0" />

Source2Viewer Avalonia port working on Steam Deck (SteamOS)

<img width="1280" height="799" alt="image" src="https://github.com/user-attachments/assets/666ff25f-be88-4990-830b-354046aec90e" />
<img width="1280" height="800" alt="image" src="https://github.com/user-attachments/assets/16c9e184-a1f1-46a5-99f5-e778f7059de4" />
<img width="1280" height="800" alt="image" src="https://github.com/user-attachments/assets/fa425c63-b62d-4090-a691-035f803970e4" />
<img width="1279" height="797" alt="image" src="https://github.com/user-attachments/assets/c84639ac-6f8f-4766-8b7a-2cfe98a8882d" />

Source2Viewer Avalonia port working on Windows

<img width="1920" height="1032" alt="image" src="https://github.com/user-attachments/assets/e02ebf0c-82ef-4bca-a02a-954da99e6e70" />
<img width="1588" height="967" alt="image" src="https://github.com/user-attachments/assets/0a8f66d8-4868-4c0a-ba72-980c1a25cae4" />

Comparing side by side original Source2Viewer.WinForms and Source2Viewer.Avalonia

<img width="1919" height="1029" alt="image" src="https://github.com/user-attachments/assets/0b2efe25-7621-4231-be99-84a481ca32df" />


## Downloads

Every release has the Windows build (`gui-windows-x64.zip`) and a cross-platform build of the same viewer for Windows (`gui-avalonia-windows-x64.zip`) and Linux (`Source2Viewer-x86_64.AppImage`), which has all of the tools above. Both builds check for updates and install them from the About dialog, after verifying that the download was built by this repository's release workflow.

The AppImage runs on any recent distribution without installing anything: make it executable (`chmod +x Source2Viewer-x86_64.AppImage`) and start it. It needs FUSE, which most desktops have; without it, start it with `--appimage-extract-and-run`.

## Credits

- [Source 2 Viewer / ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat) and its original authors and contributors, whose work this fork is built entirely on top of.
- [fvckmindself](https://github.com/fvckmindself) for [VmdlExtractor](https://github.com/fvckmindself/VmdlExtractor), the custom VMDL extraction pipeline this fork integrates.
- [ibcsz12](https://github.com/ibcsz12) for improving Export character assets (items_game.txt).
- [J0nathan550](https://github.com/J0nathan550) for this fork and for implementing Source2Viewer.Avalonia port. 

## License

Contents of this repository are available under the [MIT license](LICENSE), except for the `Tests/Files` folder, which contains files that have likely come from Valve's games.

Third party code and assets used by this project are listed in [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).

This project is not affiliated with Valve Software. Source 2 is a trademark and/or registered trademark of Valve Corporation.
