using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tricera360;

byte[] basic = [1, 128, 0, 255, 128, 0x21, 0x91, 1, 37, 208];
Check(PadInput.TryParse(basic, ConnectionKind.Bluetooth, out PadInput bt), "Basic Bluetooth parse");
Check(bt.Kind == ReportKind.BasicBluetooth && bt.L2 == 37 && bt.R2 == 208, "Basic report offsets");
Check(bt.Buttons == (PadButtons.A | PadButtons.Up | PadButtons.Right | PadButtons.L1 |
    PadButtons.Back | PadButtons.R3 | PadButtons.Guide), "Cross, diagonal D-pad, and shoulder buttons");
byte[] padded = new byte[78];
basic.CopyTo(padded, 0);
Check(PadInput.TryParse(padded, ConnectionKind.Bluetooth, out PadInput windowsBt) && windowsBt == bt,
    "Padded Windows basic Bluetooth report");

byte[] usb = new byte[64];
usb[0] = 1;
usb[1] = 128; usb[2] = 0; usb[3] = 255; usb[4] = 128;
usb[5] = 37; usb[6] = 208;
usb[8] = 0x21; usb[9] = 0x91; usb[10] = 1;
Check(PadInput.TryParse(usb, ConnectionKind.Usb, out PadInput fromUsb)
    && fromUsb == bt with { Kind = ReportKind.Usb }, "USB report offsets");
byte[] extended = new byte[78];
Array.Copy(usb, 1, extended, 2, usb.Length - 1);
extended[0] = 0x31;
Check(PadInput.TryParse(extended, ConnectionKind.Bluetooth, out PadInput fromExtended)
    && fromExtended == bt with { Kind = ReportKind.ExtendedBluetooth }, "Extended Bluetooth report offsets");
Check(!PadInput.TryParse(extended.AsSpan(0, 20), ConnectionKind.Bluetooth, out _), "Truncated extended report");
Check(!PadInput.TryParse(basic.AsSpan(0, 6), ConnectionKind.Bluetooth, out _), "Truncated basic report");
Check(!PadInput.TryParse(basic, ConnectionKind.Usb, out _), "Truncated USB report");
Check(!PadInput.TryParse([0x32, 0, 0, 0, 0, 0, 0, 0, 0, 0], ConnectionKind.Bluetooth, out _),
    "Unrelated report ignored");

Check(PlayStationModels.FromProductId(0x05C4) == PadModel.DualShock4 &&
    PlayStationModels.FromProductId(0x09CC) == PadModel.DualShock4 &&
    PlayStationModels.FromProductId(0x0CE6) == PadModel.DualSense &&
    PlayStationModels.FromProductId(0x0DF2) == PadModel.DualSenseEdge &&
    PlayStationModels.FromProductId(0x0000) is null, "Controller model IDs");
byte[] ds4Basic = [1, 121, 132, 247, 8, 0x48, 0x22, 1, 48, 243];
Check(PadInput.TryParse(ds4Basic, ConnectionKind.Bluetooth, out PadInput ds4, PadModel.DualShock4) &&
    ds4 == new PadInput(121, 132, 247, 8, 48, 243,
        PadButtons.B | PadButtons.R1 | PadButtons.Start | PadButtons.Guide, ReportKind.BasicBluetooth),
    "DS4 basic Bluetooth axes, button bytes, Share/Options and triggers");
byte[] ds4Usb = new byte[64];
ds4Basic.CopyTo(ds4Usb, 0);
Check(PadInput.TryParse(ds4Usb, ConnectionKind.Usb, out PadInput ds4Wired, PadModel.DualShock4) &&
    ds4Wired == ds4 with { Kind = ReportKind.Usb }, "DS4 USB common body");
byte[] ds4Extended = new byte[78];
ds4Extended[0] = 0x11;
ds4Extended[1] = 0xC0;
Array.Copy(ds4Basic, 1, ds4Extended, 3, 9);
Check(PadInput.TryParse(ds4Extended, ConnectionKind.Bluetooth, out PadInput ds4Full, PadModel.DualShock4) &&
    ds4Full == ds4 with { Kind = ReportKind.ExtendedBluetooth }, "DS4 extended Bluetooth three-byte header");
Check(!PadInput.TryParse(ds4Extended.AsSpan(0, 77), ConnectionKind.Bluetooth, out _, PadModel.DualShock4) &&
    !PadInput.TryParse(ds4Extended, ConnectionKind.Bluetooth, out _, PadModel.DualSense) &&
    !PadInput.TryParse(extended, ConnectionKind.Bluetooth, out _, PadModel.DualShock4),
    "Model-specific and truncated extended packets rejected");

XboxFrame frame = XboxFrame.From(bt, 5, StickInversion.None);
Check(frame.Lx == 0 && frame.Ly == short.MaxValue && frame.Rx == short.MaxValue &&
    frame.Ry == 0 && frame.L2 == 37 && frame.R2 == 208, "Xbox stick directions and independent triggers");
PadInput center = new(131, 124, 126, 131, 0, 0, PadButtons.None, ReportKind.BasicBluetooth);
Check(XboxFrame.From(center, 5, StickInversion.None) == default, "Radial deadzone clears center drift");
Check(XboxFrame.From(center with { Ry = 255 }, 5, StickInversion.None).Ry < 0, "Normal look Y");
Check(XboxFrame.From(center with { Ry = 255 }, 5, StickInversion.RightY).Ry > 0, "Inverted look Y");

PadInput offCenter = center with { Lx = 192, Ly = 80, Rx = 90, Ry = 160 };
XboxFrame normalSticks = XboxFrame.From(offCenter, 5, StickInversion.None);
XboxFrame flippedLeftX = XboxFrame.From(offCenter, 5, StickInversion.LeftX);
XboxFrame flippedLeftY = XboxFrame.From(offCenter, 5, StickInversion.LeftY);
XboxFrame flippedRightX = XboxFrame.From(offCenter, 5, StickInversion.RightX);
XboxFrame flippedRightY = XboxFrame.From(offCenter, 5, StickInversion.RightY);
Check(flippedLeftX.Lx < 0 && flippedLeftX with { Lx = normalSticks.Lx } == normalSticks,
    "Physical left X inversion changes only left X");
Check(flippedLeftY.Ly < 0 && flippedLeftY with { Ly = normalSticks.Ly } == normalSticks,
    "Physical left Y inversion changes only left Y");
Check(flippedRightX.Rx > 0 && flippedRightX with { Rx = normalSticks.Rx } == normalSticks,
    "Physical right X inversion changes only right X");
Check(flippedRightY.Ry > 0 && flippedRightY with { Ry = normalSticks.Ry } == normalSticks,
    "Physical right Y inversion changes only right Y");
InputLayout stickSwap = new("stick-swap-test", "Stick swap", [], swapSticks: true);
XboxFrame reversedSticks = XboxFrame.From(offCenter, 5, StickInversion.RightX, stickSwap);
Check(reversedSticks.Lx == flippedRightX.Rx && reversedSticks.Ly == normalSticks.Ry &&
    reversedSticks.Rx == normalSticks.Lx && reversedSticks.Ry == normalSticks.Ly,
    "Physical right X inversion follows the right stick to the virtual left output");
Check(XboxFrame.Axis(short.MinValue) == -1 && XboxFrame.Axis(short.MaxValue) == 1,
    "Preview coordinates cover the same signed range as Xbox output");

InputLayout faceSwap = new("face-swap-test", "Face swap", new()
{
    [PadControl.A] = PadControl.B, [PadControl.B] = PadControl.A,
    [PadControl.X] = PadControl.Y, [PadControl.Y] = PadControl.X
});
var faceButtonSwaps = new (PadButtons Physical, PadButtons Xbox)[]
{
    (PadButtons.A, PadButtons.B), (PadButtons.B, PadButtons.A),
    (PadButtons.X, PadButtons.Y), (PadButtons.Y, PadButtons.X)
};
foreach (var (physical, xbox) in faceButtonSwaps)
{
    PadInput test = ds4 with { Buttons = physical | PadButtons.Start | PadButtons.Back };
    var native = XboxFrame.From(test, 5, StickInversion.None, InputLayout.Default);
    var swapped = XboxFrame.From(test, 5, StickInversion.None, faceSwap);
    Check(swapped.Buttons == (xbox | PadButtons.Start | PadButtons.Back) &&
        swapped.L2 == native.L2 && swapped.R2 == native.R2 && swapped.Rx == native.Rx,
        "Custom face swap preserves sticks, triggers, Start and Back");
}

Dictionary<PadControl, PadControl> shoulderSwap = new()
{
    [PadControl.L1] = PadControl.L2, [PadControl.L2] = PadControl.L1,
    [PadControl.R1] = PadControl.R2, [PadControl.R2] = PadControl.R1
};
InputLayout callum = new("callum-test", "Callum", shoulderSwap);
PadInput callumInput = ds4 with { Buttons = PadButtons.L1 | PadButtons.Y, L2 = 127, R2 = 128 };
XboxFrame callumOutput = XboxFrame.From(callumInput, 5, StickInversion.None, callum);
Check(callumOutput.L2 == 255 && callumOutput.R2 == 0 &&
    callumOutput.Buttons == (PadButtons.R1 | PadButtons.Y),
    "Shoulder swap sends full pull for bumper and thresholds analog trigger at half");
XboxFrame callumReversed = XboxFrame.From(callumInput with { Buttons = PadButtons.R1, L2 = 200, R2 = 0 },
    5, StickInversion.None, callum);
Check(callumReversed.L2 == 0 && callumReversed.R2 == 255 && callumReversed.Buttons == PadButtons.L1,
    "Shoulder swap works independently on each side");
InputLayout custom = new("custom-test", "Custom", new()
{
    [PadControl.A] = PadControl.L2, [PadControl.R2] = PadControl.L2,
    [PadControl.Y] = PadControl.None
});
Check(custom.Map(ds4 with { Buttons = PadButtons.Y, L2 = 40, R2 = 121 }) == (PadButtons.None, (byte)121, (byte)0) &&
    custom.Map(ds4 with { Buttons = PadButtons.A | PadButtons.Y, L2 = 40, R2 = 121 }) ==
        (PadButtons.None, (byte)255, (byte)0), "Merged mappings use maximum trigger value and honor Unassigned");

string directory = Path.Combine(Path.GetTempPath(), "tricera360-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    File.WriteAllText(Path.Combine(directory, "controllers.json"),
        """{"green":{"Name":"Green DS5","Enabled":false,"Deadzone":15,"InvertLookY":true}}""");
    var settings = new SettingsStore(directory);
    PadSettings green = settings.GetOrCreate("green", "DualSense");
    Check(green == new PadSettings("Green DS5", false, 15, true), "0.1 settings migrate to the default layout");
    var layouts = new LayoutStore(directory);
    Check(layouts.List() is [var only] && only == InputLayout.Default && only.Name == "Default",
        "Fresh installs list only the standard mapping");
    InputLayout stored = layouts.Save("Callum", shoulderSwap, null, out string? error)!;
    Check(stored is not null && error is null && !stored.IsBuiltIn, "Save reusable profile");
    settings.Update("green", green with { LayoutId = stored!.Id, InvertLeftX = true, InvertRightX = true });
    settings.Update("white", new("White DS4", LayoutId: "nintendo"));
    Check(layouts.Save("default", [], null, out _) is null, "Default name is reserved");
    Check(layouts.Save("callum", [], null, out _) is null, "Duplicate custom names are rejected");
    Check(layouts.Save("Replacement", [], InputLayout.DefaultId, out _, swapSticks: true) is null &&
        !layouts.Get(InputLayout.DefaultId).SwapSticks, "Default cannot be overwritten even with a stick swap");
    var loadedSettings = new SettingsStore(directory);
    var loadedLayouts = new LayoutStore(directory, loadedSettings.UsesLayout(LayoutStore.LegacyNintendoId));
    PadSettings reloadedGreen = loadedSettings.GetOrCreate("green", "DualSense");
    Check(reloadedGreen.Inversion == (StickInversion.LeftX | StickInversion.RightX | StickInversion.RightY),
        "New inversion settings and the legacy right-Y value round-trip together");
    Check(loadedLayouts.Get(reloadedGreen.LayoutId)
        .Map(callumInput) == callum.Map(callumInput), "Saved mappings and controller assignment round-trip");
    var imported = loadedLayouts.Get(loadedSettings.GetOrCreate("white", "DualShock 4").LayoutId);
    Check(!imported.IsBuiltIn && imported.Name == "Nintendo" && faceButtonSwaps.All(swap =>
        imported.OutputFor((PadControl)swap.Physical) == (PadControl)swap.Xbox),
        "An assigned legacy preset survives as an editable custom layout");
    Check(new LayoutStore(directory).List().Count(layout => layout.Id == imported.Id) == 1,
        "Imported layout persists without requiring another migration");
    InputLayout edited = loadedLayouts.Save("Callum 2", new() { [PadControl.Y] = PadControl.None },
        stored.Id, out _, swapSticks: true)!;
    InputLayout reloaded = new LayoutStore(directory).Get(stored.Id);
    Check(edited is not null && edited.Id == stored.Id && reloaded.SwapSticks &&
        reloaded.OutputFor(PadControl.Y) == PadControl.None,
        "Editing a layout keeps its assignment ID");
    Check(loadedLayouts.Get("unknown-layout") == InputLayout.Default, "Missing layouts fall back to default");

    string portable = Path.Combine(directory, "UsbCopy", "Profiles");
    Check(SettingsStore.InitializePortableDirectory(portable, directory) is null &&
        new SettingsStore(portable).GetOrCreate("green", "DualSense") == reloadedGreen &&
        new LayoutStore(portable).Get(stored.Id).SwapSticks, "Both legacy files migrate into the portable folder");
    new SettingsStore(portable).Update("green", reloadedGreen with { Name = "Travel controller" });
    Check(SettingsStore.InitializePortableDirectory(portable, directory) is null &&
        new SettingsStore(portable).GetOrCreate("green", "DualSense").Name == "Travel controller" &&
        new SettingsStore(directory).GetOrCreate("green", "DualSense") == reloadedGreen,
        "Repeated startup leaves portable changes and legacy files alone");

    DateTimeOffset now = DateTimeOffset.UnixEpoch;
    var log = new DiagnosticLog(directory, Path.Combine(directory, "fallback"), clock: () => now);
    log.Info("SESSION_STARTED");
    log.Error("OUTPUT_TEST", "first", new IOException("line 1\nline 2"), "pad");
    log.Error("OUTPUT_TEST", "hidden duplicate", key: "pad");
    now = now.AddSeconds(31);
    log.Error("OUTPUT_TEST", "after interval", key: "pad");
    string logPath = log.CurrentFile!;
    string[] lines = File.ReadAllLines(logPath);
    Check(Path.GetDirectoryName(logPath) == directory && lines.Length == 3 &&
        lines[1].Contains("exception=IOException") && lines[1].Contains("line 1 line 2") &&
        lines[2].Contains("repeated=1"), "Diagnostic logs use the project folder and summarize repeated errors");

    string occupiedPath = Path.Combine(directory, "blocked");
    File.WriteAllText(occupiedPath, "A file prevents creation of this directory.");
    string fallbackFolder = Path.Combine(directory, "fallback");
    var fallbackLog = new DiagnosticLog(occupiedPath, fallbackFolder, 100);
    for (int i = 0; i < 15; i++) fallbackLog.Info("ROTATION_TEST", new string('a', 100));
    Check(Path.GetDirectoryName(fallbackLog.CurrentFile) == fallbackFolder &&
        Directory.GetFiles(fallbackFolder, "*.log").Length == 12,
        "Read-only project locations use bounded log rotation in the fallback folder");
}
finally { Directory.Delete(directory, recursive: true); }

List<FakeOutput> targets = [];
using (var hub = new OutputHub(() =>
{
    var target = new FakeOutput(targets.Count + 1);
    targets.Add(target);
    return target;
}))
{
    Check(hub.Send("green", frame).Player == 1, "First pad has its own player");
    XboxFrame other = frame with { Buttons = PadButtons.B, L2 = 200 };
    Check(hub.Send("white", other).Player == 2, "Second pad has its own player");
    Check(targets[0].Frames[0] == frame && targets[1].Frames[0] == other, "No cross-controller routing");
    hub.Send("green", frame);
    Check(targets[0].Frames.Count == 1, "Duplicate reports suppressed");
    XboxFrame ds4Remapped = XboxFrame.From(ds4, 5, StickInversion.None, faceSwap);
    Check(hub.Send("third", ds4Remapped).Active && hub.Send("fourth", callumOutput).Active,
        "Four simultaneous targets with mixed controller models and layouts");
    Check(targets[2].Frames[0] == ds4Remapped && targets[3].Frames[0] == callumOutput,
        "Per-pad layouts do not affect other outputs");
    for (int slot = 5; slot <= 8; slot++)
    {
        XboxFrame testFrame = frame with { Lx = (short)(slot * 1000) };
        OutputStatus extra = hub.Send($"pad-{slot}", testFrame);
        Check(extra.Active && extra.Player is null && targets[slot - 1].Frames.Single() == testFrame,
            "Pads 5–8 receive independent output without an XInput slot");
    }
    Check(hub.Send("ninth", frame).Issue == OutputIssue.SlotsFull && targets.Count == 8,
        "An additional pad waits once all eight virtual devices are active");
    hub.Release("green");
    Check(targets[0].Closed && targets[0].Frames[^1] == default && !targets[1].Closed,
        "Release neutralizes only the selected controller");
    Check(hub.Send("ninth", frame).Active, "Waiting controller can claim a freed output");
    targets[1].Fail = true;
    Check(!hub.Send("white", frame).Active && targets[1].Closed, "Failed target is released");
    Check(hub.Send("white", frame).Active, "Output can reconnect after failure");
}
Check(targets.TrueForAll(target => target.Closed), "Shutdown releases every output");
using var missingDriver = new OutputHub(() => throw new XboxDriverSetupException(new IOException()));
Check(missingDriver.Send("pad", frame).Issue == OutputIssue.DriverSetup, "Driver errors request ViGEmBus setup");
Console.WriteLine("Passed HID reports, four-axis inversion, stick swapping, portable migration, eight-pad slot handling, driver status, and logging.");
await UpdatePackageTests.RunAsync();

static void Check(bool condition, string detail)
{
    if (!condition) throw new InvalidOperationException(detail);
}

sealed class FakeOutput(int player) : IXboxOutput
{
    public List<XboxFrame> Frames { get; } = [];
    public bool Closed { get; private set; }
    public bool Fail { get; set; }
    public int? Player => player <= OutputHub.MaxXInputPlayers ? player : null;
    public void Send(XboxFrame frame)
    {
        if (Fail) throw new InvalidOperationException("Simulated driver failure");
        Frames.Add(frame);
    }
    public void Dispose() => Closed = true;
}
