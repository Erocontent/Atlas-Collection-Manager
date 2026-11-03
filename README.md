# Atlas Collection Manager

Atlas is an open-source Windows mod manager for standalone collections that cannot be published as Nexus Collections or installed through Mod Organizer 2. It brings collections that need game-specific installation rules into one launcher. Individual files can still come from Nexus Mods.

## Current version

**v0.19.21** supports **Dead or Alive 5: Last Round** and **Resident Evil 4 Remake**. Other Resident Evil and Capcom games, Dead or Alive 6, Shadow of the Tomb Raider, and Rise of the Tomb Raider are examples of workflows that motivate Atlas. They are outside the current supported list.

See the [English tutorial](docs/tutorial-en.md) for setup, collection activation, and AutoLink with ReShade.

## Build

On Windows with the .NET 10 SDK, run:

```powershell
dotnet publish src/Atlas.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=0.19.21
```

Atlas is distributed under the [MIT License](LICENSE).
