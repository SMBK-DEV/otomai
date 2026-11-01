using System.Text.Json;
using System.Text.RegularExpressions;

namespace NYROX.Desktop.Control;

internal sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NYROX", "Profiles");

    public ProfileStore() => Directory.CreateDirectory(_directory);

    public IReadOnlyList<ScriptProfile> List()
    {
        var profiles = new List<ScriptProfile>();
        foreach (var path in Directory.EnumerateFiles(_directory, "*.json").OrderBy(x => x))
        {
            try
            {
                var profile = JsonSerializer.Deserialize<ScriptProfile>(File.ReadAllText(path));
                if (profile is not null) profiles.Add(profile);
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
        return profiles;
    }

    public ScriptProfile? Get(string name)
    {
        if (!IsValidName(name)) return null;
        var path = Path.Combine(_directory, name + ".json");
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<ScriptProfile>(File.ReadAllText(path)); }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    public bool Save(ScriptProfile profile)
    {
        if (!IsValidName(profile.Name) ||
            string.IsNullOrWhiteSpace(profile.ScriptPath) || profile.ScriptPath.Length > 240 ||
            !Regex.IsMatch(profile.CallbackName, @"^[A-Za-z_][A-Za-z0-9_]{0,63}$") ||
            profile.IntervalMs is < 1000 or > 60000)
            return false;

        var path = Path.Combine(_directory, profile.Name + ".json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(profile, JsonOptions));
        File.Move(temp, path, true);
        return true;
    }

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && Regex.IsMatch(name, @"^[A-Za-z0-9_-]{1,48}$");
}

internal sealed record ScriptProfile(string Name, string ScriptPath, string CallbackName, int IntervalMs);
