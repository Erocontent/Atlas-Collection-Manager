namespace Atlas.Core;

public sealed record GameProfile(string Id, string Name, string Subtitle, int SteamAppId, string[] Executables, string NoteKey)
{
    public string Note => L.T(NoteKey);
}

public static class GameCatalog
{
    public static IReadOnlyList<GameProfile> All { get; } = new[]
    {
        new GameProfile("doa5", "DEAD OR ALIVE 5", "Last Round", 311730, ["game.exe", "DOA5LR.exe"], "NoteDoa"),
        new GameProfile("dmc5", "Devil May Cry 5", "RE Engine", 601150, ["DevilMayCry5.exe"], "NoteEngine"),
        new GameProfile("re4", "Resident Evil 4", "Remake", 2050650, ["re4.exe"], "NoteEngine"),
        new GameProfile("re3", "Resident Evil 3", "Remake", 952060, ["re3.exe"], "NoteEngine"),
        new GameProfile("re2", "Resident Evil 2", "Remake", 883710, ["re2.exe"], "NoteEngine"),
        new GameProfile("village", "Resident Evil Village", "RE Engine", 1196590, ["re8.exe"], "NoteEngine"),
        new GameProfile("mhwilds", "Monster Hunter Wilds", "RE Engine", 2246340, ["MonsterHunterWilds.exe"], "NoteEngine")
    };
    public static IReadOnlyList<GameProfile> Available {get;}=Array.AsReadOnly(All.Where(p=>p.Id is "doa5" or "re4").ToArray());
    public static bool IsAvailable(string id)=>Available.Any(p=>p.Id==id);
    public static GameProfile Get(string id) => All.SingleOrDefault(p => p.Id == id) ?? throw new InvalidOperationException(L.T("UnknownGame"));
}
