using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tricera360;

internal sealed class LayoutStore
{
    public const string LegacyNintendoId = "nintendo";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<PadControl>() }
    };
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Dictionary<string, InputLayout> _layouts = new(StringComparer.Ordinal)
    {
        [InputLayout.DefaultId] = InputLayout.Default
    };
    private string? _warning;

    private sealed record StoredLayout(string Id, string Name, Dictionary<PadControl, PadControl> Overrides,
        bool SwapSticks = false);

    public LayoutStore(string? directory = null, bool restoreLegacyNintendo = false)
    {
        _path = Path.Combine(directory ?? SettingsStore.DefaultDirectory, "layouts.json");
        Load();
        if (restoreLegacyNintendo && !_layouts.ContainsKey(LegacyNintendoId)) ImportLegacyNintendo();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var layouts = JsonSerializer.Deserialize<StoredLayout[]>(File.ReadAllText(_path), JsonOptions);
            foreach (StoredLayout? stored in layouts ?? [])
            {
                if (stored is null || string.IsNullOrWhiteSpace(stored.Id) || !ValidName(stored.Name)
                    || stored.Overrides is null || _layouts.ContainsKey(stored.Id)
                    || NameTaken(stored.Name, null)) continue;
                _layouts.Add(stored.Id, new InputLayout(stored.Id, stored.Name.Trim(), stored.Overrides, stored.SwapSticks));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            _warning = "Saved layouts could not be loaded.";
            DiagnosticLog.Session.Error("LAYOUTS_LOAD_FAILED", error: error, key: "layouts");
        }
    }

    private void ImportLegacyNintendo()
    {
        string name = "Nintendo";
        for (int suffix = 2; NameTaken(name, null); suffix++) name = $"Nintendo {suffix}";
        _layouts.Add(LegacyNintendoId, new InputLayout(LegacyNintendoId, name, new()
        {
            [PadControl.A] = PadControl.B, [PadControl.B] = PadControl.A,
            [PadControl.X] = PadControl.Y, [PadControl.Y] = PadControl.X
        }));
        if (_warning is not null) return;
        try { WriteLayouts(_layouts.Values); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _warning = "The imported layout is active but could not be saved.";
            DiagnosticLog.Session.Error("LAYOUT_IMPORT_SAVE_FAILED", error: exception, key: "layouts");
        }
    }

    public string? Warning { get { lock (_gate) return _warning; } }

    public InputLayout Get(string? id)
    {
        lock (_gate)
            return id is not null && _layouts.TryGetValue(id, out InputLayout? layout)
                ? layout : InputLayout.Default;
    }

    public InputLayout[] List()
    {
        lock (_gate)
            return _layouts.Values.Where(layout => !layout.IsBuiltIn)
                .OrderBy(layout => layout.Name, StringComparer.OrdinalIgnoreCase)
                .Prepend(InputLayout.Default).ToArray();
    }

    public InputLayout? Save(string name, Dictionary<PadControl, PadControl> overrides,
        string? updateId, out string? error, bool swapSticks = false)
    {
        lock (_gate)
        {
            name = name.Trim();
            error = null;
            if (!ValidName(name)) error = "Enter a layout name of 1–36 characters.";
            else if (updateId is not null &&
                (!_layouts.TryGetValue(updateId, out InputLayout? existing) || existing.IsBuiltIn))
                error = "Choose a saved layout to update, or save this as a new layout.";
            else if (NameTaken(name, updateId)) error = "That name is already in use. Pick a different layout name.";
            if (error is not null) return null;

            var layout = new InputLayout(updateId ?? Guid.NewGuid().ToString("N"), name, overrides, swapSticks);
            var proposed = _layouts.Values.Where(current => current.Id != layout.Id).Append(layout);
            try { WriteLayouts(proposed); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error = "The layout could not be saved. See the diagnostic log.";
                DiagnosticLog.Session.Error("LAYOUT_SAVE_FAILED", error: exception, key: "layouts");
                return null;
            }

            _layouts[layout.Id] = layout;
            _warning = null;
            return layout;
        }
    }

    private void WriteLayouts(IEnumerable<InputLayout> layouts)
    {
        var stored = layouts.Where(layout => !layout.IsBuiltIn)
            .Select(layout => new StoredLayout(layout.Id, layout.Name, layout.CopyOverrides(), layout.SwapSticks)).ToArray();
        SettingsStore.WriteJson(_path, stored, JsonOptions);
    }

    private bool NameTaken(string name, string? exceptId) => _layouts.Values.Any(layout =>
        layout.Id != exceptId && string.Equals(layout.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 36;
}
