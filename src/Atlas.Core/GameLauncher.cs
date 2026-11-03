using System.Diagnostics;

namespace Atlas.Core;

public static class GameLauncher
{
    public static ProcessStartInfo Prepare(GameProfile profile, string folder)
    {
        folder = ModLibrary.ValidateGame(folder, profile);
        var executable = profile.Executables.Select(name => SafePaths.Resolve(folder, name)).First(File.Exists);
        return new ProcessStartInfo { FileName = executable, WorkingDirectory = folder, UseShellExecute = true };
    }
}
