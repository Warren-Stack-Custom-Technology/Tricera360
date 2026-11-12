using System;
using System.Collections.Generic;

namespace Tricera360;

internal interface IXboxOutput : IDisposable
{
    int? Player { get; }
    void Send(XboxFrame frame);
}

internal enum OutputIssue { None, SlotsFull, DriverSetup, Other }

internal readonly record struct OutputStatus(bool Active, int? Player, string? Error, OutputIssue Issue = OutputIssue.None);

internal sealed class XboxDriverSetupException(Exception inner)
    : Exception("ViGEmBus is missing or incompatible.", inner);

internal sealed class XboxSlotsFullException(Exception inner)
    : Exception("Windows has no free Xbox controller slot.", inner);

internal sealed class OutputHub(Func<IXboxOutput> connect, DiagnosticLog? log = null) : IDisposable
{
    public const int MaxXInputPlayers = 4;
    public const int MaxXboxOutputs = 8;
    private const string SlotsMessage = "Turn off an active Xbox output or disconnect another virtual controller to free a device slot.";
    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _sessions = new();
    private bool _disposed;

    private sealed class Session(IXboxOutput device)
    {
        public IXboxOutput Device { get; } = device;
        public XboxFrame? Previous { get; set; }
        public int? Player { get; set; }
        public long NextPlayerCheck { get; set; }
    }

    public OutputStatus Send(string id, XboxFrame frame)
    {
        lock (_gate)
        {
            if (_disposed) return default;
            try
            {
                if (!_sessions.TryGetValue(id, out Session? session))
                {
                    if (_sessions.Count >= MaxXboxOutputs) return SlotsFull(id);
                    session = new Session(connect());
                    _sessions.Add(id, session);
                }

                if (session.Previous != frame)
                {
                    session.Device.Send(frame);
                    session.Previous = frame;
                }
                long now = Environment.TickCount64;
                if (now >= session.NextPlayerCheck)
                {
                    session.Player = session.Device.Player;
                    session.NextPlayerCheck = now + 1000;
                }
                return new(true, session.Player, null);
            }
            catch (XboxSlotsFullException)
            {
                Release(id);
                return SlotsFull(id);
            }
            catch (Exception error)
            {
                log?.Error("XBOX_OUTPUT_FAILED", $"pad={ShortId(id)}", error.GetBaseException(), $"{id}:{error.GetType().Name}");
                Release(id);
                return error is XboxDriverSetupException
                    ? new(false, null, "Install or repair ViGEmBus to create virtual Xbox controllers.", OutputIssue.DriverSetup)
                    : new(false, null, "Windows could not start or update this Xbox controller. See the diagnostic log.", OutputIssue.Other);
            }
        }
    }

    private OutputStatus SlotsFull(string id)
    {
        log?.Warning("XBOX_SLOTS_FULL", $"pad={ShortId(id)} active={_sessions.Count} appLimit={MaxXboxOutputs}", key: id);
        return new(false, null, SlotsMessage, OutputIssue.SlotsFull);
    }

    public void Release(string id)
    {
        lock (_gate)
        {
            if (!_sessions.Remove(id, out Session? session)) return;
            try { session.Device.Send(default); }
            catch (Exception error) { log?.Warning("XBOX_NEUTRALIZE_FAILED", $"pad={ShortId(id)}", error, id); }
            try { session.Device.Dispose(); }
            catch (Exception error) { log?.Error("XBOX_RELEASE_FAILED", $"pad={ShortId(id)}", error, id); }
        }
    }

    private static string ShortId(string id) => id[..Math.Min(6, id.Length)];

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (string id in new List<string>(_sessions.Keys)) Release(id);
        }
    }
}
