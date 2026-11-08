using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Voltray.Core;
using Voltray.Ipc;
using Voltray.Settings.ViewModels;

namespace Voltray.Settings.Pages;

public sealed partial class SettingsPage : UserControl
{
    static readonly int[] Intervals = [1, 5, 15, 30];

    bool _applying; // true while copying host state into the controls, so their events don't echo it back

    public SettingsPage()
    {
        InitializeComponent();
        foreach (var m in Intervals) IntervalBox.Items.Add(new ComboBoxItem { Content = m == 1 ? "1 minuto" : $"{m} minutos", Tag = m });

        IsEnabled = App.Host.State != null;
        App.Host.StateReceived += Apply;
        if (App.Host.State is { } s) Apply(s);
        Unloaded += (_, _) => App.Host.StateReceived -= Apply;
    }

    void Apply(StateDto state)
    {
        _applying = true;
        try
        {
            IsEnabled = true;
            var s = state.Settings;
            IconStyleBox.SelectedItem = IconStyleBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.IconStyle.ToString())
                ?? IconStyleBox.Items[0];
            SelectByTag(ChargingAnimationBox, s.ChargingAnimation.ToString());
            PercentSignSwitch.IsOn = s.ShowPercentSign;
            ColorNumberSwitch.IsOn = s.ColorNumber;
            AutostartSwitch.IsOn = s.StartWithWindows;
            int i = Array.IndexOf(Intervals, s.RefreshMinutes);
            IntervalBox.SelectedIndex = i >= 0 ? i : 1;
            NotifyLowSwitch.IsOn = s.NotifyLowBattery;
            NotifyChargedSwitch.IsOn = s.NotifyCharged;
            SelectByTag(TrayModeBox, s.TrayMode.ToString());
            SelectByTag(DisconnectedBox, s.Disconnected.ToString());
            ShowRules(s.AlertRules);
            // Rules only matter while low-battery alerts are on: show them, but read-only, when off.
            _rulesEnabled = AddRuleButton.IsEnabled = s.NotifyLowBattery;
            foreach (var row in RuleList.Children.OfType<Grid>()) EnableRow(row);
        }
        finally { _applying = false; }
    }

    void OnIconStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || IconStyleBox.SelectedItem is not ComboBoxItem { Tag: string tag } || !Enum.TryParse<IconStyle>(tag, out var style)) return;
        _ = App.Host.ChangeSettingsAsync(s => s.IconStyle = style);
    }

    void OnChargingAnimationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || ChargingAnimationBox.SelectedItem is not ComboBoxItem { Tag: string tag } || !Enum.TryParse<ChargingAnimation>(tag, out var a)) return;
        _ = App.Host.ChangeSettingsAsync(s => s.ChargingAnimation = a);
    }

    void OnPercentSignToggled(object sender, RoutedEventArgs e)
    {
        if (_applying) return;
        bool on = PercentSignSwitch.IsOn;
        _ = App.Host.ChangeSettingsAsync(s => s.ShowPercentSign = on);
    }

    void OnColorNumberToggled(object sender, RoutedEventArgs e)
    {
        if (_applying) return;
        bool on = ColorNumberSwitch.IsOn;
        _ = App.Host.ChangeSettingsAsync(s => s.ColorNumber = on);
    }

    void OnAutostartToggled(object sender, RoutedEventArgs e)
    {
        if (_applying) return;
        bool on = AutostartSwitch.IsOn;
        _ = App.Host.ChangeSettingsAsync(s => s.StartWithWindows = on);
    }

    void OnNotifyLowToggled(object sender, RoutedEventArgs e)
    {
        if (_applying) return;
        bool on = NotifyLowSwitch.IsOn;
        _ = App.Host.ChangeSettingsAsync(s => s.NotifyLowBattery = on);
    }

    void OnNotifyChargedToggled(object sender, RoutedEventArgs e)
    {
        if (_applying) return;
        bool on = NotifyChargedSwitch.IsOn;
        _ = App.Host.ChangeSettingsAsync(s => s.NotifyCharged = on);
    }

    static void SelectByTag(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? box.Items[0];

    void OnTrayModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || TrayModeBox.SelectedItem is not ComboBoxItem { Tag: string tag } || !Enum.TryParse<TrayMode>(tag, out var mode)) return;
        _ = App.Host.ChangeSettingsAsync(s => s.TrayMode = mode);
    }

    void OnDisconnectedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || DisconnectedBox.SelectedItem is not ComboBoxItem { Tag: string tag } || !Enum.TryParse<DisconnectedInTray>(tag, out var rule)) return;
        _ = App.Host.ChangeSettingsAsync(s => s.Disconnected = rule);
    }

    // ---- Low-battery alert rules: one row each (limit, message, test, remove) ----

    sealed record RuleRow(NumberBox Percent, TextBox Message, ComboBox Priority)
    {
        public bool Urgent => Priority.SelectedIndex == 1;
    }

    readonly List<RuleRow> _ruleRows = [];
    bool _rulesEnabled = true;

    void EnableRow(Grid row)
    {
        foreach (var child in row.Children)
            foreach (var control in child is Panel p ? p.Children.OfType<Control>() : child is Control c ? [c] : [])
                control.IsEnabled = _rulesEnabled;
    }

    /// Rebuilds the rows only when the rules changed elsewhere, so typing in a row keeps its focus.
    void ShowRules(List<AlertRule> rules)
    {
        var ordered = rules.OrderByDescending(r => r.Percent).ToList();
        if (ordered.Count == _ruleRows.Count && ordered.Zip(_ruleRows).All(p => p.First.Percent == (int)p.Second.Percent.Value && (p.First.Message ?? "") == p.Second.Message.Text.Trim() && p.First.Urgent == p.Second.Urgent))
            return;
        RuleList.Children.Clear();
        _ruleRows.Clear();
        foreach (var rule in ordered) AddRuleRow(rule);
    }

    void AddRuleRow(AlertRule rule)
    {
        var percent = new NumberBox
        {
            Value = rule.Percent, Minimum = 1, Maximum = 99, SmallChange = 5, Width = 110,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(percent, "Limite do aviso, em porcentagem");
        var message = new TextBox { Text = rule.Message ?? "", PlaceholderText = "Bateria baixa", VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(message, "Mensagem do aviso");
        var priority = new ComboBox { Items = { "Normal", "Urgente" }, SelectedIndex = rule.Urgent ? 1 : 0, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(priority, "Prioridade do aviso");
        ToolTipService.SetToolTip(priority, "Urgente pode aparecer mesmo no Não incomodar, se o Windows permitir para o Voltray");
        var row = new RuleRow(percent, message, priority);

        var test = new Button { Content = "Testar", VerticalAlignment = VerticalAlignment.Center };
        test.Click += (_, _) => _ = App.Host.TestNotificationAsync((int)percent.Value, message.Text, row.Urgent);
        var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(remove, "Remover aviso");
        ToolTipService.SetToolTip(remove, "Remover aviso");

        var grid = new Grid { ColumnSpacing = 8 };
        foreach (var w in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        var pctPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        pctPanel.Children.Add(percent);
        pctPanel.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(pctPanel);
        Grid.SetColumn(message, 1); grid.Children.Add(message);
        Grid.SetColumn(priority, 2); grid.Children.Add(priority);
        Grid.SetColumn(test, 3); grid.Children.Add(test);
        Grid.SetColumn(remove, 4); grid.Children.Add(remove);

        remove.Click += (_, _) => { _ruleRows.Remove(row); RuleList.Children.Remove(grid); SaveRules(); };
        percent.ValueChanged += (_, _) => { if (double.IsNaN(percent.Value)) percent.Value = rule.Percent; else SaveRules(); };
        message.LostFocus += (_, _) => SaveRules();
        priority.SelectionChanged += (_, _) => SaveRules();

        RuleList.Children.Add(grid);
        _ruleRows.Add(row);
        EnableRow(grid);
    }

    void SaveRules()
    {
        if (_applying) return;
        var rules = _ruleRows.Select(r => new AlertRule
        {
            Percent = (int)Math.Clamp(r.Percent.Value, 1, 99),
            Message = string.IsNullOrWhiteSpace(r.Message.Text) ? null : r.Message.Text.Trim(),
            Urgent = r.Urgent,
        }).ToList();
        _ = App.Host.ChangeSettingsAsync(s => s.AlertRules = rules);
    }

    void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        int lowest = _ruleRows.Count > 0 ? (int)_ruleRows.Min(r => r.Percent.Value) : 25;
        AddRuleRow(new AlertRule { Percent = Math.Max(1, lowest - 5) });
        SaveRules();
    }

    void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || IntervalBox.SelectedItem is not ComboBoxItem { Tag: int minutes }) return;
        _ = App.Host.ChangeSettingsAsync(s => s.RefreshMinutes = minutes);
    }
}
