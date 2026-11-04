# Atlas Collection Manager

Atlas is an open-source Windows mod manager for standalone collections that require game-specific installation rules. Individual files can still come from Nexus Mods.

## Current source version

**v0.21.5** supports **Dead or Alive 5: Last Round**, **Resident Evil 4 Remake**, and **Devil May Cry 5**. See the [changelog](CHANGELOG.md) for changes in this Windows beta.

See the [English tutorial](docs/tutorial-en.md) for setup and collection activation.

## Build

On Windows with the .NET 10 SDK, run:

```powershell
dotnet publish src/Atlas.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Atlas is distributed under the [MIT License](LICENSE).
