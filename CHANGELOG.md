# Atlas Collection Manager 0.21.5 Beta

### Devil May Cry 5

- Added Devil May Cry 5 to the game library, with character filters and editable character assignments.
- Improved import routing for wrapped loose-file mods and added variant selection for supported archives.
- Added a choice of which mod supplies overlapping message files when the selected skins can otherwise coexist.
- Added support for the official REFramework nightly archive as a DLL-only import for non-VR use. Loose-file loading remains a game-side setting.

### Collections and downloads

- Added direct HTTPS archive downloads for collections exported by Atlas Collection Editor 0.4. Atlas verifies the declared SHA-256 and imports downloads disabled.
- Redesigned collection import and activation reviews with compact rows, clearer archive labels, and fixed action footers.
- Condensed collection progress and actions into a single panel. Retry, cancel, and source controls appear when relevant.
- Reduced repeated file verification when applying or removing collections, while retaining final checks and recovery.

### Mod library

- Reworked batch review with a compact layout for small selections and separate conflict details.
- Streamlined disabling a single file mod while preserving dependency checks and file restoration.

### Installation

Extract the Windows x64 ZIP and run AtlasCollectionManager.exe. See the [player guide](https://github.com/Erocontent/Atlas-Collection-Manager/blob/main/docs/tutorial-en.md) for setup.
