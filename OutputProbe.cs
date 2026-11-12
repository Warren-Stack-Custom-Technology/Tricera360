using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;

namespace Tricera360;

internal static class OutputProbe
{
    public static int Run()
    {
        using var factory = new VigemOutputFactory();
        using var outputs = new OutputHub(factory.Connect, DiagnosticLog.Session);
        var active = new List<(string Id, XboxFrame Frame)>();
        string? failure = null;
        for (int i = 1; i <= OutputHub.MaxXboxOutputs; i++)
        {
            string id = $"test-{i}";
            XboxFrame frame = new(Lx: (short)(2500 * i), Ly: 0, Rx: 0, Ry: 0,
                L2: 0, R2: 0, Buttons: PadButtons.None);
            OutputStatus status = outputs.Send(id, frame);
            if (!status.Active)
            {
                failure = status.Error;
                break;
            }
            active.Add((id, frame));
        }

        if (active.Count > 0) Thread.Sleep(1100);
        int live = 0;
        int players = 0;
        foreach (var (id, frame) in active)
        {
            OutputStatus status = outputs.Send(id, frame with { Lx = (short)-frame.Lx });
            if (status.Active) live++;
            else failure ??= status.Error;
            if (status.Player.HasValue) players++;
            DiagnosticLog.Session.Info("OUTPUT_TEST_DEVICE", $"id={id} active={status.Active} xinputPlayer={status.Player?.ToString() ?? "none"}");
        }
        DiagnosticLog.Session.Info("OUTPUT_TEST_RESULT", $"created={active.Count} reporting={live} xinputPlayers={players}");

        string summary = $"ViGEmBus accepted reports for {live} of {OutputHub.MaxXboxOutputs} virtual Xbox controllers.\n"
            + $"Windows assigned {players} XInput player slots.\n\n"
            + "Check Windows Game Controllers (joy.cpl) while this dialog is open. Select OK to disconnect the test controllers.";
        if (failure is not null) summary += $"\n\n{failure}";
        bool passed = live == OutputHub.MaxXboxOutputs;
        MessageBox.Show(summary, "Tricera360 output test", MessageBoxButton.OK,
            passed ? MessageBoxImage.Information : MessageBoxImage.Warning);
        return passed ? 0 : 1;
    }
}
