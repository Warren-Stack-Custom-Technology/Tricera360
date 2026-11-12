using System;
using System.Collections.Generic;

namespace Tricera360;

// A, B, X and Y identify the physical positions of Cross, Circle, Square and Triangle.
internal enum PadControl
{
    None = 0, A = (int)PadButtons.A, B = (int)PadButtons.B, X = (int)PadButtons.X,
    Y = (int)PadButtons.Y, Up = (int)PadButtons.Up, Down = (int)PadButtons.Down,
    Left = (int)PadButtons.Left, Right = (int)PadButtons.Right,
    L1 = (int)PadButtons.L1, R1 = (int)PadButtons.R1, L2 = 1 << 16, R2 = 1 << 17,
    L3 = (int)PadButtons.L3, R3 = (int)PadButtons.R3, Back = (int)PadButtons.Back,
    Start = (int)PadButtons.Start, Guide = (int)PadButtons.Guide
}

internal sealed class InputLayout
{
    public const string DefaultId = "xbox-playstation";
    public const int DigitalThreshold = 128;

    public static readonly IReadOnlyList<PadControl> Inputs = Array.AsReadOnly<PadControl>(
    [
        PadControl.A, PadControl.B, PadControl.X, PadControl.Y,
        PadControl.L1, PadControl.L2, PadControl.R1, PadControl.R2,
        PadControl.Up, PadControl.Down, PadControl.Left, PadControl.Right,
        PadControl.L3, PadControl.R3, PadControl.Back, PadControl.Start, PadControl.Guide
    ]);

    public static readonly InputLayout Default = new(DefaultId, "Default", []);

    private readonly Dictionary<PadControl, PadControl> _overrides = new();
    private readonly (PadControl Source, PadControl Target)[] _routes;

    public InputLayout(string id, string name, Dictionary<PadControl, PadControl> overrides, bool swapSticks = false)
    {
        Id = id;
        Name = name;
        SwapSticks = swapSticks;
        foreach (var (source, target) in overrides)
        {
            if (source != PadControl.None && Enum.IsDefined(source)
                && Enum.IsDefined(target) && source != target)
                _overrides.Add(source, target);
        }
        var routes = new List<(PadControl, PadControl)>(Inputs.Count);
        foreach (PadControl source in Inputs)
        {
            PadControl target = OutputFor(source);
            if (target != PadControl.None) routes.Add((source, target));
        }
        _routes = routes.ToArray();
    }

    public string Id { get; }
    public string Name { get; }
    public bool SwapSticks { get; }
    public bool IsBuiltIn => Id == DefaultId;
    public PadControl OutputFor(PadControl source) => _overrides.GetValueOrDefault(source, source);
    public Dictionary<PadControl, PadControl> CopyOverrides() => new(_overrides);

    public (PadButtons Buttons, byte L2, byte R2) Map(PadInput input)
    {
        if (_overrides.Count == 0) return (input.Buttons, input.L2, input.R2);
        PadButtons buttons = PadButtons.None;
        byte l2 = 0, r2 = 0;
        foreach (var (source, target) in _routes)
        {
            byte value = source switch
            {
                PadControl.L2 => input.L2,
                PadControl.R2 => input.R2,
                _ => (input.Buttons & (PadButtons)source) != 0 ? byte.MaxValue : (byte)0
            };

            if (target == PadControl.L2) l2 = Math.Max(l2, value);
            else if (target == PadControl.R2) r2 = Math.Max(r2, value);
            else if (value >= DigitalThreshold) buttons |= (PadButtons)target;
        }
        return (buttons, l2, r2);
    }

    public static string InputName(PadControl control, PadModel model, bool compact = false) => control switch
    {
        PadControl.A => compact ? "✕" : "Cross (bottom)",
        PadControl.B => compact ? "○" : "Circle (right)",
        PadControl.X => compact ? "□" : "Square (left)",
        PadControl.Y => compact ? "△" : "Triangle (top)",
        PadControl.Up => compact ? "↑" : "D-pad up",
        PadControl.Down => compact ? "↓" : "D-pad down",
        PadControl.Left => compact ? "←" : "D-pad left",
        PadControl.Right => compact ? "→" : "D-pad right",
        PadControl.Back => model == PadModel.DualShock4 ? "Share" : "Create",
        PadControl.Start => "Options",
        PadControl.Guide => "PS",
        _ => control.ToString()
    };

    public static string OutputName(PadControl control, bool compact = false) => control switch
    {
        PadControl.None => compact ? "Off" : "Unassigned",
        PadControl.A => compact ? "A" : "A (bottom)",
        PadControl.B => compact ? "B" : "B (right)",
        PadControl.X => compact ? "X" : "X (left)",
        PadControl.Y => compact ? "Y" : "Y (top)",
        PadControl.L1 => compact ? "LB" : "LB (left bumper)",
        PadControl.R1 => compact ? "RB" : "RB (right bumper)",
        PadControl.L2 => compact ? "LT" : "LT (left trigger)",
        PadControl.R2 => compact ? "RT" : "RT (right trigger)",
        PadControl.L3 => compact ? "LS" : "Left stick click",
        PadControl.R3 => compact ? "RS" : "Right stick click",
        PadControl.Up => compact ? "↑" : "D-pad up",
        PadControl.Down => compact ? "↓" : "D-pad down",
        PadControl.Left => compact ? "←" : "D-pad left",
        PadControl.Right => compact ? "→" : "D-pad right",
        PadControl.Guide => "Guide",
        _ => control.ToString()
    };
}
