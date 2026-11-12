using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HidSharp;

namespace Tricera360;

internal sealed class PadWorker : IDisposable
{
    private readonly HidDevice _device;
    private readonly OutputHub _outputs;
    private readonly Func<bool> _globallyEnabled;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConnectionKind _connection;
    private readonly PadModel _model;
    private volatile PadConfiguration _configuration;
    private volatile PadSnapshot _snapshot;

    public PadWorker(HidDevice device, string id, PadModel model, PadConfiguration configuration, OutputHub outputs,
        Func<bool> globallyEnabled)
    {
        _device = device;
        Id = id;
        _model = model;
        _configuration = configuration;
        _outputs = outputs;
        _globallyEnabled = globallyEnabled;
        _connection = GetConnection(device.DevicePath, device.GetMaxInputReportLength());
        _snapshot = new(id, configuration.Settings, configuration.Layout, model, _connection, null, default, PadState.Connecting);
        DiagnosticLog.Session.Info("CONTROLLER_FOUND", $"pad={id[..6]} model={model} transport={_connection}");
        Completion = Task.Run(ReadLoop);
    }

    public string Id { get; }
    public Task Completion { get; }
    public PadSnapshot Snapshot => _snapshot;
    public PadConfiguration Configuration { get => _configuration; set => _configuration = value; }

    private static ConnectionKind GetConnection(string path, int reportLength)
    {
        if (path.Contains("00001124", StringComparison.OrdinalIgnoreCase)
            || path.Contains("bthenum", StringComparison.OrdinalIgnoreCase)
            || path.Contains("vid&0002054c", StringComparison.OrdinalIgnoreCase))
            return ConnectionKind.Bluetooth;
        if (reportLength == 64) return ConnectionKind.Usb;
        return reportLength > 64 ? ConnectionKind.Bluetooth : ConnectionKind.Unknown;
    }

    private void ReadLoop()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    if (!_device.TryOpen(out HidStream? stream))
                    {
                        DiagnosticLog.Session.Error("HID_OPEN_FAILED", $"pad={Id[..6]} model={_model}", key: Id);
                        Publish(null, default, PadState.InputFailed,
                            "Close other controller mappers, then reconnect the controller.");
                    }
                    else
                    {
                        using (stream) ReadStream(stream);
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException
                    or InvalidOperationException or TimeoutException)
                {
                    if (_stop.IsCancellationRequested) break;
                    DiagnosticLog.Session.Warning("HID_READ_INTERRUPTED", $"pad={Id[..6]} transport={_connection}", error, Id);
                    Publish(null, default, PadState.InputFailed, "Input stopped. Reconnect the controller if it stays unavailable.");
                }
                finally { _outputs.Release(Id); }
                if (!_stop.IsCancellationRequested) _stop.Token.WaitHandle.WaitOne(900);
            }
        }
        finally { Publish(null, default, PadState.Disconnected); }
    }

    private void ReadStream(HidStream stream)
    {
        stream.ReadTimeout = 125;
        byte[] report = new byte[Math.Max(78, _device.GetMaxInputReportLength())];
        long lastReport = Environment.TickCount64;
        long nextOutputAttempt = 0;
        long nextUiUpdate = 0;
        PadInput? input = null;
        OutputStatus output = default;
        bool previouslyEnabled = false;
        bool waitingLogged = false;

        while (!_stop.IsCancellationRequested)
        {
            int count = 0;
            try { count = stream.Read(report, 0, report.Length); }
            catch (TimeoutException) { /* Recheck the pause and input watchdog on every timeout. */ }
            if (_stop.IsCancellationRequested) return;

            long now = Environment.TickCount64;
            if (PadInput.TryParse(report.AsSpan(0, count), _connection, out PadInput parsed, _model))
            {
                if (input?.Kind != parsed.Kind || waitingLogged)
                    DiagnosticLog.Session.Info("HID_INPUT_LIVE", $"pad={Id[..6]} report={parsed.Kind} bytes={count}");
                input = parsed;
                lastReport = now;
                waitingLogged = false;
            }
            else if (count > 0)
                DiagnosticLog.Session.Warning("HID_REPORT_IGNORED", $"pad={Id[..6]} reportId=0x{report[0]:X2} bytes={count}",
                    key: $"{Id}:{report[0]}:{count}");

            if (!waitingLogged && now - lastReport >= (input.HasValue ? 2500 : 8000))
            {
                waitingLogged = true;
                DiagnosticLog.Session.Warning("HID_WAITING", $"pad={Id[..6]} transport={_connection} elapsedMs={now - lastReport}", key: Id);
            }

            PadConfiguration configuration = _configuration;
            PadSettings settings = configuration.Settings;
            bool wantsOutput = _globallyEnabled() && settings.Enabled;
            if (!wantsOutput)
            {
                if (output.Active) _outputs.Release(Id);
                output = default;
            }
            else if (input is PadInput value)
            {
                if (now - lastReport >= 2500)
                {
                    _outputs.Release(Id);
                    output = default;
                    input = null;
                }
                else if (output.Active || !previouslyEnabled || now >= nextOutputAttempt)
                {
                    XboxFrame frame = now - lastReport < 750
                        ? XboxFrame.From(value, settings.Deadzone, settings.Inversion, configuration.Layout) : default;
                    // Releasing is immediate, but new outputs are only allocated for live inputs.
                    if (output.Active || now - lastReport < 750)
                        output = _outputs.Send(Id, frame);
                    if (!output.Active) nextOutputAttempt = now + 1000;
                }
            }
            previouslyEnabled = wantsOutput;

            if (now < nextUiUpdate) continue;
            nextUiUpdate = now + 30;
            if (input is null || now - lastReport >= 750)
                Publish(null, output, PadState.Waiting, "Wake the controller or reconnect it if it is already on.");
            else if (!wantsOutput)
                Publish(input, default, PadState.Paused, "The physical controller remains available to games.");
            else if (output.Active)
                Publish(input, output, PadState.Active, output.Player.HasValue
                    ? "Games can use this pad as an Xbox 360 controller."
                    : "No XInput player slot assigned. Additional pads need a game that supports them.");
            else
                Publish(input, output, output.Issue == OutputIssue.SlotsFull ? PadState.WaitingForSlot : PadState.OutputFailed,
                    output.Error);
        }
    }

    private void Publish(PadInput? input, OutputStatus output, PadState state, string? detail = null)
    {
        ConnectionKind link = input?.Kind switch
        {
            ReportKind.BasicBluetooth or ReportKind.ExtendedBluetooth => ConnectionKind.Bluetooth,
            ReportKind.Usb => ConnectionKind.Usb,
            _ => _connection
        };
        PadConfiguration configuration = _configuration;
        PadSnapshot previous = _snapshot;
        if (previous.State != state || previous.Output.Player != output.Player || previous.Output.Active != output.Active)
            DiagnosticLog.Session.Info("CONTROLLER_STATE", $"pad={Id[..6]} from={previous.State} to={state} output={output.Active} player={output.Player?.ToString() ?? "unassigned"}");
        _snapshot = new(Id, configuration.Settings, configuration.Layout, _model, link, input, output, state, detail);
    }

    public void Stop() => _stop.Cancel();
    public void Dispose() => _stop.Dispose();
}
