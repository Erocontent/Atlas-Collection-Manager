# Atlas Collection Manager

Atlas is an open-source Windows mod manager for standalone collections that require game-specific installation rules. Individual files can still come from Nexus Mods.

## Current source version

**v0.19.22** supports **Dead or Alive 5: Last Round** and **Resident Evil 4 Remake**. This update restores a compatible ReShade runtime backed up when AutoLink replaced its DirectX 9 DLL.

See the [English tutorial](docs/tutorial-en.md) for setup and collection activation.

## Build

On Windows with the .NET 10 SDK, run:

```powershell
dotnet publish src/Atlas.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Atlas is distributed under the [MIT License](LICENSE).
