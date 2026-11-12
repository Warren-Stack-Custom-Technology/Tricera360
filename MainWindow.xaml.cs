using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Tricera360;

public partial class MainWindow : Window
{
    private readonly BridgeManager _bridge;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<PadControl, (Border Surface, TextBlock Label)> _buttons = new();
    private Row[] _rows = [];
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _trayPause;
    private System.Drawing.Icon? _trayIcon;
    private bool _syncing;
    private string? _selectedId;
    private PadSettings? _selectedSettings;
    private InputLayout? _displayedLayout;
    private PadModel _displayedModel;
    private string? _statusArt;
    private string? _footerArt;
    private string? _notice;
    private long _noticeUntil;
    private const string TrayMessage = "Minimize to keep running in the notification area. Close to release Xbox outputs.";

    public MainWindow()
    {
        InitializeComponent();
        _bridge = new BridgeManager();
        RefreshLayoutOptions(null);
        CreateButtonMonitor();
        Deadzone.ItemsSource = Enumerable.Range(0, 7)
            .Select(n => new DeadzoneOption(n * 5, $"{n * 5}%")).ToArray();
        Deadzone.SelectedValuePath = nameof(DeadzoneOption.Value);
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(45) };
        _timer.Tick += (_, _) => RefreshView();
        Loaded += (_, _) => { CreateTray(); _timer.Start(); RefreshView(); };
        SourceInitialized += (_, _) => App.ApplyDarkTitleBar(this);
    }

    private void CreateButtonMonitor()
    {
        PadControl[] controls =
        [
            PadControl.A, PadControl.B, PadControl.X, PadControl.Y, PadControl.Guide,
            PadControl.Up, PadControl.Down, PadControl.Left, PadControl.Right, PadControl.Back,
            PadControl.L1, PadControl.R1, PadControl.L3, PadControl.R3, PadControl.Start
        ];
        foreach (PadControl button in controls)
        {
            TextBlock label = new()
            {
                FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(3, 0, 3, 0)
            };
            Border surface = new()
            {
                CornerRadius = new CornerRadius(5), Margin = new Thickness(2, 2, 2, 2),
                Height = 25, Child = label, BorderThickness = new Thickness(1),
                BorderBrush = Brush("Border"), Background = Brush("Well")
            };
            Buttons.Children.Add(surface);
            _buttons[button] = (surface, label);
        }
    }

    private Brush Brush(string key) => (Brush)FindResource(key);

    private void RefreshView()
    {
        PadSnapshot[] pads = _bridge.Snapshot();
        int count = pads.Count(pad => pad.Output.Active);
        ControllersHeading.Text = pads.Length == 0 ? "Controllers" : $"Controllers · {pads.Length}";
        Summary.Text = _bridge.Enabled ? $"{count} / {OutputHub.MaxXboxOutputs} Xbox outputs" : "Outputs paused";
        Summary.Foreground = Brush(!_bridge.Enabled || count == 0 ? "Secondary" : "Accent");
        UpdateFooter();
        if (!_rows.Select(row => row.Id).SequenceEqual(pads.Select(pad => pad.Id)))
        {
            string? id = (ControllerList.SelectedItem as Row)?.Id;
            _syncing = true;
            _rows = pads.Select(pad => new Row(pad)).ToArray();
            ControllerList.ItemsSource = _rows;
            ControllerList.SelectedItem = _rows.FirstOrDefault(row => row.Id == id) ?? _rows.FirstOrDefault();
            _syncing = false;
        }
        else
        {
            for (int i = 0; i < pads.Length; i++) _rows[i].Set(pads[i]);
        }

        if (ControllerList.SelectedItem is Row row) ShowPad(row.Pad);
        else ClearDetail();
    }

    private void SelectController(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        if (ControllerList.SelectedItem is Row row) ShowPad(row.Pad);
        else ClearDetail();
    }

    private void ClearDetail()
    {
        _selectedId = null;
        _selectedSettings = null;
        Detail.Visibility = Visibility.Collapsed;
        EmptyDetail.Visibility = Visibility.Visible;
    }

    private void ShowPad(PadSnapshot pad)
    {
        if (pad.Id != _selectedId)
        {
            _syncing = true;
            _selectedId = pad.Id;
            _selectedSettings = pad.Settings;
            NameBox.Text = pad.Settings.Name;
            OutputEnabled.IsChecked = pad.Settings.Enabled;
            InvertLeftX.IsChecked = pad.Settings.InvertLeftX;
            InvertLeftY.IsChecked = pad.Settings.InvertLeftY;
            InvertRightX.IsChecked = pad.Settings.InvertRightX;
            InvertRightY.IsChecked = pad.Settings.InvertLookY;
            Deadzone.SelectedValue = pad.Settings.Deadzone;
            LayoutPicker.SelectedValue = pad.Layout.Id;
            SaveName.IsEnabled = false;
            _syncing = false;
        }
        EmptyDetail.Visibility = Visibility.Collapsed;
        Detail.Visibility = Visibility.Visible;
        DetailName.Text = _selectedSettings?.Name ?? pad.Settings.Name;
        string mode = pad.Input?.Kind switch
        {
            ReportKind.BasicBluetooth => "Bluetooth · Basic input",
            ReportKind.ExtendedBluetooth => "Bluetooth · Extended input",
            ReportKind.Usb => "USB · Input active",
            _ => LinkName(pad.Connection)
        };
        LinkMode.Text = $"{pad.Model.Name()}   ·   {mode}   ·   {pad.Id[..6]}";
        ShowStatus(pad);
        UpdateLayoutLabels(pad.Layout, pad.Model);
        DisplayInput(pad.Input, _selectedSettings ?? pad.Settings, pad.Layout);
    }

    private void ShowStatus(PadSnapshot pad)
    {
        (string art, string color, string surface) = pad.State switch
        {
            PadState.Active => ("DinoSuccess", "Accent", "Well"),
            PadState.InputFailed or PadState.OutputFailed => ("DinoError", "Warning", "ErrorSurface"),
            PadState.Connecting or PadState.Waiting or PadState.WaitingForSlot => ("DinoLoading", "PendingAccent", "Well"),
            _ => ("DinoIdle", "Secondary", "Well")
        };
        if (art == "DinoLoading" && !SystemParameters.ClientAreaAnimation) art = "DinoIdle";
        if (_statusArt != art)
        {
            PadArt.ContentTemplate = (DataTemplate)FindResource(art);
            _statusArt = art;
        }
        InputStatus.Text = pad.State == PadState.Paused && !pad.Settings.Enabled ? "Xbox output off for this pad" : pad.Status;
        StatusDetail.Text = pad.Detail ?? "Looking for controller input.";
        DriverLink.Visibility = pad.Output.Issue == OutputIssue.DriverSetup ? Visibility.Visible : Visibility.Collapsed;
        InputStatus.Foreground = Brush(color);
        StatusCard.Background = Brush(surface);
    }

    private void UpdateFooter()
    {
        string? error = _bridge.Warning;
        if (DiagnosticLog.Session.CurrentFile is null) error ??= "The diagnostic log could not be written.";
        bool showNotice = _notice is not null && Environment.TickCount64 < _noticeUntil;
        Footer.Text = error ?? (showNotice ? _notice! : TrayMessage);
        Footer.Foreground = Brush(error is null ? "Secondary" : "Warning");
        string? art = error is not null ? "DinoError" : showNotice ? "DinoSuccess" : null;
        if (_footerArt == art) return;
        _footerArt = art;
        FooterArt.Visibility = art is null ? Visibility.Collapsed : Visibility.Visible;
        FooterArt.ContentTemplate = art is null ? null : (DataTemplate)FindResource(art);
    }

    private void UpdateLayoutLabels(InputLayout layout, PadModel model)
    {
        if (ReferenceEquals(layout, _displayedLayout) && model == _displayedModel) return;
        _displayedLayout = layout;
        _displayedModel = model;
        LayoutAction.Content = layout.IsBuiltIn ? "New profile…" : "Edit profile…";
        LayoutNote.Text = layout.IsBuiltIn ? "Read-only · Standard button and stick mapping." : "";
        LayoutNote.Visibility = layout.IsBuiltIn ? Visibility.Visible : Visibility.Collapsed;
        LeftStickSource.Text = layout.SwapSticks ? "From physical right stick" : "From physical left stick";
        RightStickSource.Text = layout.SwapSticks ? "From physical left stick" : "From physical right stick";
        foreach (var (control, (surface, label)) in _buttons)
        {
            PadControl target = layout.OutputFor(control);
            string source = InputLayout.InputName(control, model);
            label.Text = MappingLabel(layout, model, control);
            surface.ToolTip = target == PadControl.None ? $"{source} is unassigned"
                : $"{source} sends Xbox {InputLayout.OutputName(target)}";
        }
        LeftTriggerLabel.Text = MappingLabel(layout, model, PadControl.L2);
        RightTriggerLabel.Text = MappingLabel(layout, model, PadControl.R2);
    }

    private static string MappingLabel(InputLayout layout, PadModel model, PadControl input)
    {
        string source = InputLayout.InputName(input, model, compact: true);
        string target = InputLayout.OutputName(layout.OutputFor(input), compact: true);
        return source == target ? source : $"{source} → {target}";
    }

    private void DisplayInput(PadInput? input, PadSettings settings, InputLayout layout)
    {
        XboxFrame frame = input is PadInput value ? XboxFrame.From(value, settings.Deadzone, settings.Inversion, layout) : default;
        double lx = XboxFrame.Axis(frame.Lx), ly = XboxFrame.Axis(frame.Ly);
        double rx = XboxFrame.Axis(frame.Rx), ry = XboxFrame.Axis(frame.Ry);
        LeftStick.Update(lx, -ly, input.HasValue);
        RightStick.Update(rx, -ry, input.HasValue);
        LeftValues.Text = $"X  {lx:0.00}    Y  {ly:0.00}";
        RightValues.Text = $"X  {rx:0.00}    Y  {ry:0.00}";
        LeftTrigger.Value = input?.L2 ?? 0;
        RightTrigger.Value = input?.R2 ?? 0;
        LeftTriggerValue.Text = $"{LeftTrigger.Value / 255:P0}";
        RightTriggerValue.Text = $"{RightTrigger.Value / 255:P0}";
        PadButtons buttons = input?.Buttons ?? PadButtons.None;
        foreach (var (button, (surface, label)) in _buttons)
        {
            bool down = (buttons & (PadButtons)button) != 0;
            surface.Background = Brush(down ? "ActiveBg" : "Well");
            surface.BorderBrush = Brush(down ? "Accent" : "Border");
            label.Foreground = Brush(down ? "Accent" : "Secondary");
        }
    }

    private void ChangeProfile(object sender, RoutedEventArgs e)
    {
        if (_syncing || _selectedSettings is not PadSettings current || _selectedId is null) return;
        SaveProfile(current with
        {
            Enabled = OutputEnabled.IsChecked == true,
            InvertLeftX = InvertLeftX.IsChecked == true,
            InvertLeftY = InvertLeftY.IsChecked == true,
            InvertRightX = InvertRightX.IsChecked == true,
            InvertLookY = InvertRightY.IsChecked == true,
            Deadzone = Deadzone.SelectedValue is int percent ? percent : current.Deadzone
        });
    }

    private void ChangeDeadzone(object sender, SelectionChangedEventArgs e) => ChangeProfile(sender, e);

    private void RefreshLayoutOptions(string? selectedLayoutId)
    {
        bool wasSyncing = _syncing;
        _syncing = true;
        LayoutPicker.ItemsSource = _bridge.Layouts();
        LayoutPicker.SelectedValue = selectedLayoutId;
        _syncing = wasSyncing;
    }

    private void ChangeLayout(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _selectedSettings is not PadSettings current ||
            LayoutPicker.SelectedItem is not InputLayout layout) return;
        SaveProfile(current with { LayoutId = layout.Id });
        if (ControllerList.SelectedItem is Row row) UpdateLayoutLabels(layout, row.Pad.Model);
    }

    private void OpenLayoutEditor(object sender, RoutedEventArgs e)
    {
        if (ControllerList.SelectedItem is not Row row || _selectedSettings is not PadSettings settings
            || LayoutPicker.SelectedItem is not InputLayout layout) return;
        string id = row.Id;
        LayoutEditor editor = new(_bridge, layout, row.Pad.Model) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is not InputLayout saved) return;

        if (_selectedId == id)
        {
            RefreshLayoutOptions(saved.Id);
            SaveProfile(settings with { LayoutId = saved.Id });
            UpdateLayoutLabels(saved, row.Pad.Model);
        }
        else
        {
            _bridge.SetSettings(id, settings with { LayoutId = saved.Id });
            RefreshLayoutOptions(_selectedSettings?.LayoutId);
        }
        _notice = $"Layout saved · {saved.Name}";
        _noticeUntil = Environment.TickCount64 + 5000;
        UpdateFooter();
    }

    private void NameChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || _selectedSettings is null) return;
        SaveName.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text) && NameBox.Text.Trim() != _selectedSettings.Name;
    }

    private void NameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        SaveControllerName(sender, e);
    }

    private void SaveControllerName(object sender, RoutedEventArgs e)
    {
        if (_selectedSettings is null || string.IsNullOrWhiteSpace(NameBox.Text)) return;
        SaveProfile(_selectedSettings with { Name = NameBox.Text.Trim() });
        NameBox.Text = _selectedSettings.Name;
        SaveName.IsEnabled = false;
    }

    private void SaveProfile(PadSettings updated)
    {
        if (_selectedId is null || _selectedSettings == updated) return;
        _selectedSettings = updated;
        _bridge.SetSettings(_selectedId, updated);
    }

    private void ToggleOutputs(object sender, RoutedEventArgs e) => ToggleOutputState();

    private void ToggleOutputState()
    {
        _bridge.Enabled = !_bridge.Enabled;
        DiagnosticLog.Session.Info(_bridge.Enabled ? "OUTPUTS_RESUMED" : "OUTPUTS_PAUSED");
        string caption = _bridge.Enabled ? "Pause outputs" : "Resume outputs";
        PauseButton.Content = caption;
        if (_trayPause is not null) _trayPause.Text = caption;
        RefreshView();
    }

    private void OpenWindowsTest(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("control.exe", "joy.cpl") { UseShellExecute = true }); }
        catch (Win32Exception error)
        {
            DiagnosticLog.Session.Error("WINDOWS_TEST_OPEN_FAILED", error: error);
            MessageBox.Show(this, "Windows Game Controllers could not open. See the diagnostic log.", "Tricera360");
        }
    }

    private void OpenLog(object sender, RoutedEventArgs e)
    {
        try
        {
            string? path = DiagnosticLog.Session.CurrentFile;
            if (path is null || !File.Exists(path))
            {
                DiagnosticLog.Session.Info("LOG_REQUESTED");
                path = DiagnosticLog.Session.CurrentFile;
            }
            if (path is null || !File.Exists(path))
            {
                MessageBox.Show(this, "Tricera360 could not write a diagnostic log in the app or user data folder.", "Tricera360");
                return;
            }
            Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path } });
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Session.Error("LOG_OPEN_FAILED", error: error);
            MessageBox.Show(this, $"The log could not open in Notepad.\n\n{DiagnosticLog.Session.CurrentFile}", "Tricera360");
        }
    }

    private void OpenDriverReleases(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(VigemOutputFactory.DriverReleasesUrl) { UseShellExecute = true }); }
        catch (Win32Exception error)
        {
            DiagnosticLog.Session.Error("VIGEM_DOWNLOAD_LINK_FAILED", error: error);
            MessageBox.Show(this, $"Open this address in your browser to download ViGEmBus.\n\n{VigemOutputFactory.DriverReleasesUrl}", "Tricera360");
        }
    }

    private void CreateTray()
    {
        if (_tray is not null) return;
        _trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
            ?? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        Forms.ContextMenuStrip menu = new();
        menu.Items.Add("Open Tricera360", null, (_, _) => Dispatcher.InvokeAsync(RestoreFromTray));
        _trayPause = new Forms.ToolStripMenuItem("Pause outputs", null, (_, _) =>
            Dispatcher.InvokeAsync(ToggleOutputState));
        menu.Items.Add(_trayPause);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.InvokeAsync(Close));
        _tray = new Forms.NotifyIcon { Icon = _trayIcon, Text = "Tricera360", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => Dispatcher.InvokeAsync(RestoreFromTray);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState != WindowState.Minimized || _tray is null) return;
        Hide();
        _timer.Stop();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _timer.Start();
        RefreshView();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
        }
        _trayIcon?.Dispose();
        _bridge.Dispose();
        base.OnClosed(e);
    }

    private static string LinkName(ConnectionKind connection) => connection switch
    {
        ConnectionKind.Bluetooth => "Bluetooth",
        ConnectionKind.Usb => "USB",
        _ => "HID"
    };

    private sealed record DeadzoneOption(int Value, string Label);

    private sealed class Row(PadSnapshot pad) : INotifyPropertyChanged
    {
        public PadSnapshot Pad { get; private set; } = pad;
        public string Id => Pad.Id;
        public string Name => Pad.Settings.Name;
        public string LayoutName => Pad.Layout.Name;
        public string Caption => $"{LinkName(Pad.Connection)}   ·   " +
            (Pad.Output.Player is int player ? $"Xbox Player {player}" : Pad.Status);
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Set(PadSnapshot updated)
        {
            string oldName = Name;
            string oldCaption = Caption;
            string oldLayout = LayoutName;
            Pad = updated;
            if (oldName != Name) PropertyChanged?.Invoke(this, new(nameof(Name)));
            if (oldCaption != Caption) PropertyChanged?.Invoke(this, new(nameof(Caption)));
            if (oldLayout != LayoutName) PropertyChanged?.Invoke(this, new(nameof(LayoutName)));
        }
    }
}
