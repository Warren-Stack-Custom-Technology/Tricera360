using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HidSharp;

namespace Tricera360;

internal sealed record PadConfiguration(PadSettings Settings, InputLayout Layout);

internal enum PadState { Connecting, Waiting, Active, Paused, WaitingForSlot, InputFailed, OutputFailed, Disconnected }

internal sealed record PadSnapshot(string Id, PadSettings Settings, InputLayout Layout, PadModel Model,
    ConnectionKind Connection,
    PadInput? Input, OutputStatus Output, PadState State, string? Detail = null)
{
    public string Status => State switch
    {
        PadState.Connecting => "Connecting",
        PadState.Waiting => "Waiting for input",
        PadState.Active => Output.Player is int player ? $"Xbox Player {player}" : "Xbox output active",
        PadState.Paused => "Output paused",
        PadState.WaitingForSlot => "Waiting for an Xbox slot",
        PadState.InputFailed => "Input unavailable",
        PadState.OutputFailed => "Xbox output unavailable",
        _ => "Disconnected"
    };
}

internal sealed class BridgeManager : IDisposable
{
    private readonly SettingsStore _store;
    private readonly LayoutStore _layouts;
    private readonly VigemOutputFactory _factory = new();
    private readonly OutputHub _outputs;
    private readonly object _gate = new();
    private readonly Dictionary<string, PadWorker> _workers = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _scanner;
    private readonly string? _storageWarning;
    private volatile string? _scanWarning;
    private volatile bool _enabled = true;

    public BridgeManager()
    {
        _storageWarning = SettingsStore.InitializePortableDirectory();
        _store = new SettingsStore();
        _layouts = new LayoutStore(restoreLegacyNintendo: _store.UsesLayout(LayoutStore.LegacyNintendoId));
        _outputs = new OutputHub(_factory.Connect, DiagnosticLog.Session);
        _scanner = Task.Run(ScanLoop);
    }

    public bool Enabled { get => _enabled; set => _enabled = value; }
    public string? Warning => _scanWarning ?? _layouts.Warning ?? _store.Warning
        ?? (_storageWarning is not null && !Directory.Exists(SettingsStore.DefaultDirectory) ? _storageWarning : null);
    public InputLayout[] Layouts() => _layouts.List();

    public PadSnapshot[] Snapshot()
    {
        lock (_gate)
            return _workers.Values.Select(worker => worker.Snapshot)
                .OrderBy(pad => pad.Settings.Name, StringComparer.OrdinalIgnoreCase).ThenBy(pad => pad.Id)
                .ToArray();
    }

    public void SetSettings(string id, PadSettings settings)
    {
        lock (_gate)
        {
            PadWorker? worker = _workers.Values.FirstOrDefault(item => item.Id == id);
            InputLayout layout = _layouts.Get(settings.LayoutId);
            settings = settings with { LayoutId = layout.Id };
            if (worker is not null) worker.Configuration = new(settings, layout);
            _store.Update(id, settings);
            DiagnosticLog.Session.Info("CONTROLLER_SETTINGS", $"pad={id[..6]} layout={layout.Id} output={settings.Enabled} deadzone={settings.Deadzone} inversion={settings.Inversion} saved={_store.Warning is null}");
        }
    }

    public InputLayout? SaveLayout(string name, Dictionary<PadControl, PadControl> overrides,
        string? updateId, out string? error, bool swapSticks)
    {
        lock (_gate)
        {
            InputLayout? layout = _layouts.Save(name, overrides, updateId, out error, swapSticks);
            if (layout is null) return null;
            DiagnosticLog.Session.Info("LAYOUT_SAVED", $"layout={layout.Id} edited={updateId is not null} remaps={layout.CopyOverrides().Count} swapSticks={layout.SwapSticks}");
            foreach (PadWorker worker in _workers.Values)
            {
                PadConfiguration current = worker.Configuration;
                if (current.Layout.Id == layout.Id) worker.Configuration = current with { Layout = layout };
            }
            return layout;
        }
    }

    private void ScanLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                HidDevice[] devices = DeviceList.Local.GetHidDevices(0x054C)
                    .Where(device => PlayStationModels.FromProductId(device.ProductID) is not null).ToArray();
                Reconcile(devices);
                if (_scanWarning is not null) DiagnosticLog.Session.Info("HID_SCAN_RECOVERED");
                _scanWarning = null;
            }
            catch (Exception error)
            {
                _scanWarning = "Could not scan Windows controllers. See the diagnostic log.";
                DiagnosticLog.Session.Error("HID_SCAN_FAILED", error: error, key: "scan");
            }
            _stop.Token.WaitHandle.WaitOne(750);
        }
    }

    private void Reconcile(HidDevice[] devices)
    {
        var present = new HashSet<string>(devices.Select(device => device.DevicePath),
            StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            if (_stop.IsCancellationRequested) return;
            // Retain a cancelling worker until it releases its target. A quick reconnect cannot
            // create two workers with the same HID identity or let the old worker remove its output.
            foreach (var (path, worker) in _workers.ToArray())
            {
                if (!present.Contains(path)) worker.Stop();
                if (worker.Completion.IsCompleted)
                {
                    _workers.Remove(path);
                    worker.Dispose();
                }
            }

            foreach (HidDevice device in devices)
            {
                if (_workers.ContainsKey(device.DevicePath) ||
                    PlayStationModels.FromProductId(device.ProductID) is not PadModel model || !IsGamepad(device))
                    continue;
                string id = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(device.DevicePath.ToUpperInvariant())));
                PadSettings settings = _store.GetOrCreate(id, model.Name());
                InputLayout layout = _layouts.Get(settings.LayoutId);
                _workers.Add(device.DevicePath, new PadWorker(device, id, model,
                    new PadConfiguration(settings with { LayoutId = layout.Id }, layout), _outputs, () => _enabled));
            }
        }
    }

    private static bool IsGamepad(HidDevice device)
    {
        try
        {
            // Generic Desktop / Game Pad. Ignore Sony's other HID collections.
            return device.GetReportDescriptor().DeviceItems.Any(item =>
                item.Usages.GetAllValues().Contains(0x00010005u));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidOperationException or NotSupportedException)
        {
            DiagnosticLog.Session.Warning("HID_DESCRIPTOR_UNAVAILABLE", $"product=0x{device.ProductID:X4}", error,
                $"{device.ProductID:X4}");
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        PadWorker[] workers;
        lock (_gate)
        {
            workers = _workers.Values.ToArray();
            foreach (PadWorker worker in workers) worker.Stop();
        }
        Task[] tasks = workers.Select(worker => worker.Completion).Append(_scanner).ToArray();
        try
        {
            if (!Task.WaitAll(tasks, 2500)) DiagnosticLog.Session.Warning("STOP_TIMEOUT", $"workers={workers.Length}");
        }
        catch (AggregateException error)
        {
            DiagnosticLog.Session.Error("STOP_TASK_FAILED", error: error.GetBaseException());
        }
        finally
        {
            foreach (PadWorker worker in workers.Where(worker => worker.Completion.IsCompleted)) worker.Dispose();
            if (_scanner.IsCompleted) _stop.Dispose();
            _outputs.Dispose();
            try { _factory.Dispose(); }
            catch (Exception error) { DiagnosticLog.Session.Error("VIGEM_CLIENT_RELEASE_FAILED", error: error); }
        }
    }
}
