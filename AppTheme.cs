using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace Tricera360;

internal sealed class AppTheme
{
    private static readonly string[] ResourceKeys =
    [
        "WindowBackground", "Panel", "Well", "Border", "ControlFill",
        "Text", "Secondary", "Accent", "ActiveBg", "Brand",
        "Selected", "SelectedBorder", "ProfileAccent", "PendingAccent", "Warning", "ErrorSurface"
    ];
    private static readonly Brush LightWordmark = MakeBrush(0x39552C);
    private static readonly Brush DarkWordmark = MakeBrush(0xB4CD88);

    private static readonly AppTheme Green = new("Green", false,
        [0x101612, 0x1A231D, 0x131C17, 0x36483B, 0x24342A,
         0xF3F7EF, 0xB0C2B3, 0x64E9EE, 0x204347, 0xB4CD88,
         0x2A3C2D, 0x789666, 0xF49BD0, 0xEDF598, 0xFF9990, 0x352724],
        0x1D2B22, 0x52725C, 0x34493D, 0x83988A);

    internal static IReadOnlyList<AppTheme> Choices { get; } =
    [
        Green,
        new("Yellow", false,
            [0x191609, 0x25200F, 0x1E1A0B, 0x564A25, 0x372D12,
             0xFFF7D9, 0xD7C89A, 0xFFDE6B, 0x4B3910, 0xF6CB57,
             0x3C3214, 0xA9893A, 0xEBCD82, 0xFFE79A, 0xFFA994, 0x42291B],
            0x2E2711, 0x8E753C, 0x524520, 0xB19A64),
        new("Brown", false,
            [0x1B120F, 0x2A1C16, 0x211610, 0x5F4335, 0x3B281E,
             0xFFF1E5, 0xD8BAA7, 0xF4BD91, 0x533323, 0xEAB486,
             0x452D22, 0xA2745B, 0xE7BDA4, 0xF9D6A2, 0xFFAAA0, 0x472A25],
            0x332219, 0x8B624C, 0x573C2E, 0xB49480),
        new("Purple / Magenta", false,
            [0x1A101E, 0x26172C, 0x201325, 0x5B3A63, 0x38203E,
             0xFFF2FF, 0xD6B9DA, 0xFFA6EA, 0x4A2350, 0xF3A5E7,
             0x452448, 0xA764A9, 0xF6BBF2, 0xFFD0EF, 0xFFAAA8, 0x492630],
            0x321D37, 0x87518E, 0x57345D, 0xAF86B0),
        new("Black", false,
            [0x090909, 0x141414, 0x0D0D0D, 0x3D3D3D, 0x242424,
             0xF7F7F7, 0xBDBDBD, 0xEFEFEF, 0x383838, 0xF2F2F2,
             0x2C2C2C, 0x868686, 0xD7D7D7, 0xEEEEEE, 0xFFAAAA, 0x352525],
            0x1D1D1D, 0x777777, 0x444444, 0xA2A2A2),
        new("White", true,
            [0xF3F3F3, 0xFFFFFF, 0xF6F6F6, 0xBBBBBB, 0xEEEEEE,
             0x191919, 0x535353, 0x303030, 0xDEDEDE, 0x202020,
             0xE6E6E6, 0x737373, 0x454545, 0x525252, 0xA22720, 0xFFF0EE],
            0xECECEC, 0x777777, 0xB3B3B3, 0x686868)
    ];

    private static string SettingsPath => Path.Combine(SettingsStore.DefaultDirectory, "appearance.json");
    private readonly ResourceDictionary _resources = new();

    public string Name { get; }
    internal bool IsLight { get; }
    internal Brush Accent => (Brush)_resources["Accent"];
    internal Brush StickFace { get; }
    internal Pen StickOutline { get; }
    internal Pen StickGrid { get; }
    internal Brush StickQuietDot { get; }
    internal static AppTheme Current { get; private set; } = Green;

    private AppTheme(string name, bool isLight, uint[] colors, uint stickFace, uint stickOutline,
        uint stickGrid, uint stickQuietDot)
    {
        Name = name;
        IsLight = isLight;
        for (int i = 0; i < ResourceKeys.Length; i++)
            _resources.Add(ResourceKeys[i], MakeBrush(colors[i]));
        _resources.Add("Wordmark", isLight ? LightWordmark : DarkWordmark);
        StickFace = MakeBrush(stickFace);
        StickOutline = MakePen(stickOutline, 1.1);
        StickGrid = MakePen(stickGrid, 1);
        StickQuietDot = MakeBrush(stickQuietDot);
    }

    internal static AppTheme Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return Green;
            string? name = JsonSerializer.Deserialize<string>(File.ReadAllText(SettingsPath));
            return Choices.FirstOrDefault(theme => theme.Name == name) ?? Green;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            DiagnosticLog.Session.Warning("THEME_LOAD_FAILED", error: error);
            return Green;
        }
    }

    internal bool TrySave()
    {
        try
        {
            SettingsStore.WriteJson(SettingsPath, Name, JsonSerializerOptions.Default);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Session.Warning("THEME_SAVE_FAILED", error: error);
            return false;
        }
    }

    internal void Apply()
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        int index = dictionaries.IndexOf(Current._resources);
        Current = this;
        if (index < 0) dictionaries.Add(_resources);
        else dictionaries[index] = _resources;
    }

    private static SolidColorBrush MakeBrush(uint rgb)
    {
        SolidColorBrush brush = new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        brush.Freeze();
        return brush;
    }

    private static Pen MakePen(uint color, double width)
    {
        Pen pen = new(MakeBrush(color), width);
        pen.Freeze();
        return pen;
    }
}
