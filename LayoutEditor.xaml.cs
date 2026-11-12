using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Tricera360;

public partial class LayoutEditor : Window
{
    private readonly BridgeManager _bridge;
    private readonly InputLayout _source;
    private readonly Dictionary<PadControl, ComboBox> _bindings = new();

    internal LayoutEditor(BridgeManager bridge, InputLayout source, PadModel model)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => App.ApplyDarkTitleBar(this);
        _bridge = bridge;
        _source = source;
        SwapSticks.IsChecked = source.SwapSticks;
        Heading.Text = source.IsBuiltIn ? "New profile from Default" : $"Edit {source.Name}";
        if (source.IsBuiltIn)
        {
            SaveExisting.Visibility = Visibility.Collapsed;
            SaveCopy.Content = "Save and assign";
            SaveCopy.IsDefault = true;
            SaveCopy.Background = (Brush)FindResource("ActiveBg");
        }
        else
        {
            LayoutName.Text = source.Name;
            SaveExisting.IsDefault = true;
        }
        var options = InputLayout.Inputs.Prepend(PadControl.None)
            .Select(control => new OutputOption(control, InputLayout.OutputName(control))).ToArray();
        foreach (PadControl input in InputLayout.Inputs)
            AddBinding(input, model, options);
        Loaded += (_, _) => LayoutName.Focus();
    }

    internal InputLayout? Result { get; private set; }

    private void AddBinding(PadControl input, PadModel model, OutputOption[] options)
    {
        Grid row = new() { Margin = new Thickness(4, 3, 4, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        string inputName = InputLayout.InputName(input, model);
        TextBlock label = new() { Text = inputName, VerticalAlignment = VerticalAlignment.Center };
        ComboBox picker = new()
        {
            ItemsSource = options, SelectedValuePath = nameof(OutputOption.Control),
            ItemTemplate = (DataTemplate)FindResource("OutputOption"), MinHeight = 24
        };
        picker.SelectedValue = _source.OutputFor(input);
        AutomationProperties.SetName(picker, $"{inputName} Xbox output");
        Grid.SetColumn(picker, 1);
        row.Children.Add(label);
        row.Children.Add(picker);
        MappingRows.Children.Add(row);
        _bindings.Add(input, picker);
    }

    private void SwapShoulders(object sender, RoutedEventArgs e)
    {
        Swap(PadControl.L1, PadControl.L2);
        Swap(PadControl.R1, PadControl.R2);
    }

    private void Swap(PadControl a, PadControl b)
    {
        object first = _bindings[a].SelectedValue;
        _bindings[a].SelectedValue = _bindings[b].SelectedValue;
        _bindings[b].SelectedValue = first;
    }

    private void SaveNew(object sender, RoutedEventArgs e) => Save(null);
    private void SaveUpdate(object sender, RoutedEventArgs e) => Save(_source.Id);

    private void Save(string? id)
    {
        var mapping = _bindings.ToDictionary(pair => pair.Key,
            pair => pair.Value.SelectedValue is PadControl output ? output : pair.Key);
        Result = _bridge.SaveLayout(LayoutName.Text, mapping, id, out string? error, SwapSticks.IsChecked == true);
        if (Result is null)
        {
            ErrorText.Text = error;
            ErrorPanel.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }

    private sealed record OutputOption(PadControl Control, string Label);
}
