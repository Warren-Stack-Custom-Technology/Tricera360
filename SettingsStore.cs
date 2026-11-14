using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tricera360;

internal sealed record PadSettings(string Name, bool Enabled = true, int Deadzone = 5,
    bool InvertLookY = false, string LayoutId = InputLayout.DefaultId,
    bool InvertLeftX = false, bool InvertLeftY = false, bool InvertRightX = false)
{
    // Preserve the older JSON field name for inversion of the physical right stick's Y axis.
    [JsonIgnore]
    public StickInversion Inversion => (InvertLeftX ? StickInversion.LeftX : StickInversion.None)
        | (InvertLeftY ? StickInversion.LeftY : StickInversion.None)
        | (InvertRightX ? StickInversion.RightX : StickInversion.None)
        | (InvertLookY ? StickInversion.RightY : StickInversion.None);
}

internal sealed class SettingsStore
{
    internal static string InstallDirectory { get; } =
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    internal static string DefaultDirectory { get; } = Path.Combine(InstallDirectory, "Profiles");
    private static string LegacyDirectory { get; } = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "DS5Windows");
    private readonly string _path;
    private readonly Dictionary<string, PadSettings> _pads = new();
    private readonly object _gate = new();
    private string? _warning;

    internal static string? InitializePortableDirectory(string? directory = null, string? legacyDirectory = null)
    {
        directory ??= DefaultDirectory;
        legacyDirectory ??= LegacyDirectory;
        if (Directory.Exists(directory)) return null;

        // Publish both legacy files together so an interrupted import can be retried safely.
        string stage = directory + ".import-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(stage);
            int imported = 0;
            foreach (string name in new[] { "layouts.json", "controllers.json" })
            {
                string source = Path.Combine(legacyDirectory, name);
                if (!File.Exists(source)) continue;
                File.Copy(source, Path.Combine(stage, name));
                imported++;
            }
            Directory.Move(stage, directory);
            if (imported > 0) DiagnosticLog.Session.Info("PROFILES_IMPORTED", $"files={imported}");
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (Directory.Exists(directory)) return null;
            DiagnosticLog.Session.Error("PORTABLE_PROFILES_UNAVAILABLE", error: error);
            return "The Profiles folder could not be created beside Tricera360. Run it from a writable folder to save changes.";
        }
        finally
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                DiagnosticLog.Session.Warning("PROFILE_IMPORT_CLEANUP_FAILED", error: error);
            }
        }
    }

    public SettingsStore(string? directory = null)
    {
        _path = Path.Combine(directory ?? DefaultDirectory, "controllers.json");
        try
        {
            if (!File.Exists(_path)) return;
            var data = JsonSerializer.Deserialize<Dictionary<string, PadSettings>>(File.ReadAllText(_path));
            if (data is null) return;
            foreach (var (id, pad) in data)
            {
                if (pad is null || string.IsNullOrWhiteSpace(pad.Name)) continue;
                _pads[id] = pad with
                {
                    Deadzone = Math.Clamp(pad.Deadzone, 0, 30),
                    LayoutId = string.IsNullOrWhiteSpace(pad.LayoutId) ? InputLayout.DefaultId : pad.LayoutId
                };
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            _warning = "Saved controller settings could not be loaded.";
            DiagnosticLog.Session.Error("CONTROLLER_SETTINGS_LOAD_FAILED", error: error, key: "settings");
        }
    }

    public string? Warning
    {
        get { lock (_gate) return _warning; }
    }

    public bool UsesLayout(string layoutId)
    {
        lock (_gate)
            return _pads.Values.Any(pad => pad.LayoutId == layoutId);
    }

    public PadSettings GetOrCreate(string id, string model)
    {
        lock (_gate)
        {
            if (_pads.TryGetValue(id, out PadSettings? saved)) return saved;
            int number = 1;
            while (_pads.Values.Any(p => p.Name == $"{model} {number}")) number++;
            PadSettings created = new($"{model} {number}");
            _pads[id] = created;
            Save();
            return created;
        }
    }

    public void Update(string id, PadSettings settings)
    {
        lock (_gate)
        {
            _pads[id] = settings;
            Save();
        }
    }

    private void Save()
    {
        try
        {
            WriteJson(_path, _pads, new JsonSerializerOptions { WriteIndented = true });
            _warning = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _warning = "Controller settings could not be saved to the Profiles folder.";
            DiagnosticLog.Session.Error("CONTROLLER_SETTINGS_SAVE_FAILED", error: error, key: "settings");
        }
    }

    internal static void WriteJson<T>(string path, T value, JsonSerializerOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, options));
        File.Move(temp, path, overwrite: true);
    }
}
