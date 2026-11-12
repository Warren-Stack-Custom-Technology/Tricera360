using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360.Exceptions;

namespace Tricera360;

internal sealed class VigemOutputFactory : IDisposable
{
    public const string DriverReleasesUrl = "https://github.com/nefarius/ViGEmBus/releases";
    // OutputHub serializes calls to this factory and to the controller targets.
    private ViGEmClient? _client;

    public IXboxOutput Connect()
    {
        IXbox360Controller? controller = null;
        try
        {
            _client ??= new ViGEmClient();
            controller = _client.CreateXbox360Controller();
            controller.AutoSubmitReport = false;
            controller.Connect();
            return new VigemOutput(controller);
        }
        catch (Exception failure)
        {
            try { (controller as IDisposable)?.Dispose(); }
            catch (Exception error) { DiagnosticLog.Session.Warning("VIGEM_CONNECT_CLEANUP_FAILED", error: error, key: "driver"); }
            if (failure is VigemBusNotFoundException or VigemBusVersionMismatchException)
                throw new XboxDriverSetupException(failure);
            if (failure is VigemNoFreeSlotException) throw new XboxSlotsFullException(failure);
            throw;
        }
    }

    public void Dispose() => _client?.Dispose();

    private sealed class VigemOutput(IXbox360Controller controller) : IXboxOutput
    {
        public int? Player
        {
            get
            {
                try
                {
                    int slot = controller.UserIndex;
                    return slot is >= 0 and < OutputHub.MaxXInputPlayers ? slot + 1 : null;
                }
                catch (Xbox360UserIndexNotReportedException)
                {
                    // A virtual device can be live before or without an XInput player index.
                    return null;
                }
                catch (Exception error)
                {
                    DiagnosticLog.Session.Warning("VIGEM_PLAYER_QUERY_FAILED", error: error, key: "driver");
                    return null;
                }
            }
        }

        public void Send(XboxFrame frame)
        {
            controller.ButtonState = (ushort)frame.Buttons;
            controller.LeftThumbX = frame.Lx;
            controller.LeftThumbY = frame.Ly;
            controller.RightThumbX = frame.Rx;
            controller.RightThumbY = frame.Ry;
            controller.LeftTrigger = frame.L2;
            controller.RightTrigger = frame.R2;
            controller.SubmitReport();
        }

        public void Dispose()
        {
            try { controller.Disconnect(); }
            catch (Exception error) { DiagnosticLog.Session.Warning("VIGEM_DISCONNECT_FAILED", error: error, key: "driver"); }
            try { (controller as IDisposable)?.Dispose(); }
            catch (Exception error) { DiagnosticLog.Session.Warning("VIGEM_DISPOSE_FAILED", error: error, key: "driver"); }
        }
    }
}
