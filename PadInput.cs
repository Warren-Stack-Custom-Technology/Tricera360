using System;

namespace Tricera360;

internal enum ConnectionKind { Bluetooth, Usb, Unknown }
internal enum ReportKind { BasicBluetooth, ExtendedBluetooth, Usb }
internal enum PadModel { DualSense, DualSenseEdge, DualShock4 }

internal static class PlayStationModels
{
    public static PadModel? FromProductId(int productId) => productId switch
    {
        0x0CE6 => PadModel.DualSense,
        0x0DF2 => PadModel.DualSenseEdge,
        0x05C4 or 0x09CC => PadModel.DualShock4,
        _ => null
    };

    public static string Name(this PadModel model) => model switch
    {
        PadModel.DualSenseEdge => "DualSense Edge",
        PadModel.DualShock4 => "DualShock 4",
        _ => "DualSense"
    };
}

[Flags]
internal enum PadButtons : ushort
{
    None = 0, Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008,
    Start = 0x0010, Back = 0x0020, L3 = 0x0040, R3 = 0x0080,
    L1 = 0x0100, R1 = 0x0200, Guide = 0x0400, A = 0x1000, B = 0x2000,
    X = 0x4000, Y = 0x8000
}

internal readonly record struct PadInput(byte Lx, byte Ly, byte Rx, byte Ry,
    byte L2, byte R2, PadButtons Buttons, ReportKind Kind)
{
    private static readonly PadButtons[] Hats =
    [
        PadButtons.Up, PadButtons.Up | PadButtons.Right, PadButtons.Right,
        PadButtons.Down | PadButtons.Right, PadButtons.Down,
        PadButtons.Down | PadButtons.Left, PadButtons.Left, PadButtons.Up | PadButtons.Left
    ];

    public static bool TryParse(ReadOnlySpan<byte> bytes, ConnectionKind connection, out PadInput input,
        PadModel model = PadModel.DualSense)
    {
        input = default;
        if (bytes.IsEmpty) return false;

        bool isDs4 = model == PadModel.DualShock4;
        if (bytes[0] == (isDs4 ? 0x11 : 0x31) && bytes.Length >= 78)
        {
            input = isDs4 ? CompactReport(bytes, 3, ReportKind.ExtendedBluetooth)
                : FullDualSenseReport(bytes, 2, ReportKind.ExtendedBluetooth);
            return true;
        }

        if (bytes[0] != 0x01 || bytes.Length < 10) return false;
        if (connection == ConnectionKind.Usb ||
            (connection == ConnectionKind.Unknown && bytes.Length == 64))
        {
            if (bytes.Length < 64) return false;
            input = isDs4 ? CompactReport(bytes, 1, ReportKind.Usb)
                : FullDualSenseReport(bytes, 1, ReportKind.Usb);
        }
        else
        {
            // Windows may pad a short Bluetooth report to the maximum HID buffer size.
            input = CompactReport(bytes, 1, ReportKind.BasicBluetooth);
        }
        return true;
    }

    // DS4's common packet and the DS5 basic packet put triggers after the button bytes.
    private static PadInput CompactReport(ReadOnlySpan<byte> bytes, int offset, ReportKind kind) =>
        new(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3],
            bytes[offset + 7], bytes[offset + 8],
            ReadButtons(bytes[offset + 4], bytes[offset + 5], bytes[offset + 6]), kind);

    private static PadInput FullDualSenseReport(ReadOnlySpan<byte> bytes, int offset, ReportKind kind) =>
        new(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3],
            bytes[offset + 4], bytes[offset + 5],
            ReadButtons(bytes[offset + 7], bytes[offset + 8], bytes[offset + 9]), kind);

    private static PadButtons ReadButtons(byte face, byte middle, byte special)
    {
        int hat = face & 15;
        PadButtons buttons = hat < Hats.Length ? Hats[hat] : PadButtons.None;
        // Sony's face nibble is Square, Cross, Circle, Triangle.
        if ((face & 0x10) != 0) buttons |= PadButtons.X;
        if ((face & 0x20) != 0) buttons |= PadButtons.A;
        if ((face & 0x40) != 0) buttons |= PadButtons.B;
        if ((face & 0x80) != 0) buttons |= PadButtons.Y;
        if ((middle & 0x01) != 0) buttons |= PadButtons.L1;
        if ((middle & 0x02) != 0) buttons |= PadButtons.R1;
        if ((middle & 0x10) != 0) buttons |= PadButtons.Back;
        if ((middle & 0x20) != 0) buttons |= PadButtons.Start;
        if ((middle & 0x40) != 0) buttons |= PadButtons.L3;
        if ((middle & 0x80) != 0) buttons |= PadButtons.R3;
        if ((special & 0x01) != 0) buttons |= PadButtons.Guide;
        return buttons;
    }

    public static double Axis(byte value) => (value - 128) / (value < 128 ? 128d : 127d);
}

[Flags]
internal enum StickInversion { None = 0, LeftX = 1, LeftY = 2, RightX = 4, RightY = 8 }

internal readonly record struct XboxFrame(short Lx, short Ly, short Rx, short Ry,
    byte L2, byte R2, PadButtons Buttons)
{
    public static XboxFrame From(PadInput input, int deadzonePercent, StickInversion inversion, InputLayout? layout = null)
    {
        layout ??= InputLayout.Default;
        double deadzone = Math.Clamp(deadzonePercent, 0, 30) / 100d;
        var left = MapStick(input.Lx, input.Ly, deadzone,
            inversion.HasFlag(StickInversion.LeftX), inversion.HasFlag(StickInversion.LeftY));
        var right = MapStick(input.Rx, input.Ry, deadzone,
            inversion.HasFlag(StickInversion.RightX), inversion.HasFlag(StickInversion.RightY));
        if (layout.SwapSticks) (left, right) = (right, left);
        var (buttons, l2, r2) = layout.Map(input);
        return new(left.X, left.Y, right.X, right.Y, l2, r2, buttons);
    }

    public static double Axis(short value) => value / (value < 0 ? 32768d : 32767d);

    private static (short X, short Y) MapStick(byte rawX, byte rawY, double deadzone, bool invertX, bool invertY)
    {
        double x = PadInput.Axis(rawX) * (invertX ? -1 : 1);
        double y = PadInput.Axis(rawY) * (invertY ? 1 : -1);
        double magnitude = Math.Sqrt(x * x + y * y);
        if (magnitude <= deadzone) return (0, 0);

        double scale = (Math.Min(magnitude, 1) - deadzone) / (1 - deadzone) / magnitude;
        return (ToShort(x * scale), ToShort(y * scale));
    }

    private static short ToShort(double value) =>
        (short)Math.Round(Math.Clamp(value, -1, 1) * (value < 0 ? 32768 : 32767));
}
