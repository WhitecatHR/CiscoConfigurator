using Microsoft.Win32;
using System.Text;
using System.Text.Json;
using System.IO;
using System.IO.Ports;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Input;

namespace CiscoConfigGuiWpf;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, Control> _valueControls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FrameworkElement> _fieldWrappers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FieldDefinition> _fieldDefinitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBox> _previewControls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CheckBox> _moduleChecks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _moduleCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FrameworkElement> _moduleRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ModuleDefinition>> _modulesByTab = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TabItem> _tabsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Expander> _moduleExpanders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _moduleStatusBadges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> _moduleStatusTexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _tabSearchText = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> _tabActiveCountTexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> _tabModuleCountTexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FrameworkElement> _tabEmptyStates = new(StringComparer.OrdinalIgnoreCase);
    private TextBlock? _dashboardDeviceText;
    private TextBlock? _dashboardActiveModulesText;
    private TextBlock? _dashboardValidationText;
    private readonly HashSet<string> _favoriteModules = new(StringComparer.OrdinalIgnoreCase);
    private RichTextBox? _configurationPreviewBox;
    private string _configurationPreviewText = string.Empty;
    private TextBox? _peerRequirementsBox;
    private string _peerRequirementsText = string.Empty;
    private IReadOnlyList<DuplicateConfigIssue> _lastDuplicateConfigIssues = Array.Empty<DuplicateConfigIssue>();
    private bool _duplicateCheckHasRun;
    private SerialPortSettings _serialSettings = new("", 9600, 8, Parity.None, StopBits.One, 35);
    private bool _serialTestMode;
    private TextBox? _importConfigBox;
    private TextBox? _importResultBox;
    private ImportedConfigAnalysis? _lastImportAnalysis;
    private IReadOnlyList<UiValidationIssue> _currentValidationIssues = Array.Empty<UiValidationIssue>();
    private bool _updatingStpPreview;

    private static readonly HashSet<string> VrfOnlyModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "vrfDefs", "vrfSvi", "vrfStaticRoutes", "vrfOspf", "vrfOspfv3", "vrfBgp"
    };

    private static readonly HashSet<string> NoVrfOnlyModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "staticRoutes", "ospf", "bgp", "isis", "ospfv3", "ipv6RoutingProtocols"
    };

    private static readonly string[] ModuleTabOrder =
    {
        "Basis", "Management", "Interfaces", "Switching", "Routing", "IPv6/DHCP/ACL", "Security/WAN"
    };

    private static readonly Dictionary<string, string[]> ModuleOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Basis"] = new[] { "basic", "banner" },
        ["Management"] = new[] { "ssh", "userRights", "aaa", "radiusTacacs", "monitoringBase" },
        ["Interfaces"] = new[] { "interfaceRoles", "interfaceProfiles", "interfaces", "ranges", "trunkUplink", "etherChannelExt", "subinterfaces", "routerStick", "qinq", "qosBasic" },
        ["Switching"] = new[] { "vlanIpPlan", "vlans", "accessBaseline", "stpExtended", "voip", "portSecurityExt", "switchSec", "switchSecExt", "errdisableRecovery" },
        ["Routing"] = new[] { "routingBase", "staticRoutes", "ospf", "ospfAdvanced", "isis", "bgp", "bgpAdvanced", "routeMapFilter", "fhrp", "ipSlaTracking", "vrfLite", "vrfDefs", "vrfSvi", "vrfStaticRoutes", "vrfOspf", "vrfOspfv3", "vrfBgp", "mpls" },
        ["IPv6/DHCP/ACL"] = new[] { "dhcp", "aclAssistant", "acl", "ipv6", "ipv6Extended", "ospfv3", "ipv6RoutingProtocols" },
        ["Security/WAN"] = new[] { "security", "nat", "zoneFirewall", "dmzAssistant", "wanFailover", "vpn", "greIpsec", "vpnAdvanced", "customCommands" }
    };

    private sealed record DuplicateConfigIssue(string Context, string Command, int Count, IReadOnlyList<int> Lines);
    private sealed record UiValidationIssue(
        string Tab,
        string ModuleName,
        string ModuleTitle,
        string FieldName,
        string FieldLabel,
        string Message);

    public MainWindow()
    {
        InitializeComponent();


        InitTopBar();
        BuildDashboardTab();
        BuildModuleTabs();
        BuildConfigurationTab();
        BuildPeerRequirementsTab();
        BuildImportConfigTab();
        BuildCommandTab();
        BuildCheckTab();
        if (MainTabs.Items.Count > 0) MainTabs.SelectedIndex = 0;
        HookButtons();
        ApplyFilters();
        UpdateConditionalFieldVisibility();
        RefreshStpPreview();
    }

    private void InitTopBar()
    {
        DeviceTypeCombo.ItemsSource = new[] { "Router", "L3-Switch", "L2-Switch" };
        DeviceTypeCombo.SelectedIndex = 0;
        ConfigModeCombo.ItemsSource = new[] { "Ohne VRF", "Mit VRF" };
        ConfigModeCombo.SelectedIndex = 0;
        WriteMemCombo.ItemsSource = new[] { "Ja", "Nein" };
        WriteMemCombo.SelectedIndex = 0;

        DeviceTypeCombo.SelectionChanged += (_, _) => { InvalidateGeneratedState(); ApplyFilters(); UpdateConditionalFieldVisibility(); RefreshStpPreview(); };
        ConfigModeCombo.SelectionChanged += (_, _) => { InvalidateGeneratedState(); ApplyFilters(); UpdateConditionalFieldVisibility(); RefreshStpPreview(); };

        DeviceTypeCombo.ToolTip = TooltipBuilder.Create("Gerätetyp", "Zweck:\nFiltert Module passend zum Zielgerät.\n\nRouter:\nRouting, WAN, NAT, VPN, OSPF/BGP.\n\nL3-Switch:\nSwitching, SVIs, Routing.\n\nL2-Switch:\nSwitching, Management, ACL-Basis.");
        ConfigModeCombo.ToolTip = TooltipBuilder.Create("Konfigurationsmodus", "Ohne VRF:\nNormale globale IPv4/IPv6-Konfiguration.\n\nMit VRF:\nVRF-Definitionen, VRF-SVIs, VRF-Routen und VRF-Routingprotokolle werden eingeblendet; globale Routingmodule werden ausgeblendet.");
        WriteMemCombo.ToolTip = TooltipBuilder.Create("write memory", "Zweck:\nFügt am Ende write memory hinzu.\n\nHinweis:\nFür Labor meist praktisch. Produktiv nur setzen, wenn die Konfiguration vorher geprüft wurde.");
        ValidationTextBlock.MouseLeftButtonUp += (_, _) => NavigateToFirstValidationIssue();
    }

    private void HookButtons()
    {
        CopyButton.Click += async (_, _) => await CopyGeneratedConfigAsync();
        SaveButton.Click += async (_, _) => await SaveTextExportAsync();
        SaveTemplateButton.Click += (_, _) => SaveTemplate();
        LoadTemplateButton.Click += (_, _) => LoadTemplate();
        ResetAllButton.Click += (_, _) => ResetAllInputs();
    }

    private void BuildDashboardTab()
    {
        var tab = new TabItem { Header = "⌂  Übersicht" };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var root = new StackPanel { Margin = new Thickness(2, 2, 8, 2) };
        scroll.Content = root;

        var hero = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var heroGrid = new Grid();
        heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heroGrid.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(232, 121, 26)),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 0, 16, 0)
        });
        var heroText = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        heroText.Children.Add(new TextBlock { Text = "Konfigurationsübersicht", FontSize = 26, FontWeight = FontWeights.Bold });
        heroText.Children.Add(new TextBlock
        {
            Text = "Gerät auswählen, benötigte Module aktivieren, Eingaben prüfen und die fertige Cisco-Konfiguration ausgeben.",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });
        Grid.SetColumn(heroText, 1);
        heroGrid.Children.Add(heroText);
        hero.Child = heroGrid;
        root.Children.Add(hero);

        var stats = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 12) };
        _dashboardDeviceText = new TextBlock { Text = "Router", FontSize = 19, FontWeight = FontWeights.Bold };
        _dashboardActiveModulesText = new TextBlock { Text = "0 Module", FontSize = 19, FontWeight = FontWeights.Bold };
        _dashboardValidationText = new TextBlock { Text = "Bereit", FontSize = 19, FontWeight = FontWeights.Bold };
        stats.Children.Add(CreateDashboardStatCard("Zielgerät", _dashboardDeviceText, "Wird über die Kopfzeile ausgewählt."));
        stats.Children.Add(CreateDashboardStatCard("Aktive Module", _dashboardActiveModulesText, "Nur aktive Module werden exportiert."));
        stats.Children.Add(CreateDashboardStatCard("Validierung", _dashboardValidationText, "Warnungen führen direkt zum betroffenen Feld."));
        root.Children.Add(stats);

        var workflowCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var workflowStack = new StackPanel();
        workflowStack.Children.Add(new TextBlock { Text = "Empfohlener Ablauf", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });
        var workflow = new UniformGrid { Columns = 4 };
        workflow.Children.Add(CreateWorkflowCard("1", "Ziel festlegen", "Router, L3- oder L2-Switch und VRF-Modus auswählen."));
        workflow.Children.Add(CreateWorkflowCard("2", "Module aktivieren", "Nur die tatsächlich benötigten Funktionsbereiche einschalten."));
        workflow.Children.Add(CreateWorkflowCard("3", "Werte prüfen", "Pflichtfelder, STP-Konflikte und doppelte Befehle kontrollieren."));
        workflow.Children.Add(CreateWorkflowCard("4", "Ausgeben", "Vorschau öffnen, kopieren, exportieren oder per COM senden."));
        workflowStack.Children.Add(workflow);
        workflowCard.Child = workflowStack;
        root.Children.Add(workflowCard);

        var quickCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16)
        };
        var quickStack = new StackPanel();
        quickStack.Children.Add(new TextBlock { Text = "Direktzugriff", FontSize = 18, FontWeight = FontWeights.Bold });
        quickStack.Children.Add(new TextBlock
        {
            Text = "Häufig verwendete Bereiche ohne Umweg öffnen.",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            FontSize = 12,
            Margin = new Thickness(0, 3, 0, 8)
        });
        var quick = new WrapPanel();
        quick.Children.Add(CreateQuickNavigationButton("Basis", () => NavigateToTab("Basis"), true));
        quick.Children.Add(CreateQuickNavigationButton("Switching", () => NavigateToTab("Switching")));
        quick.Children.Add(CreateQuickNavigationButton("Routing", () => NavigateToTab("Routing")));
        quick.Children.Add(CreateQuickNavigationButton("Spanning Tree", () => OpenModule("Switching", "stpExtended")));
        quick.Children.Add(CreateQuickNavigationButton("Gegenstelle", () => NavigateToTab("Gegenstelle")));
        quick.Children.Add(CreateQuickNavigationButton("Import", () => NavigateToTab("Import")));
        quick.Children.Add(CreateQuickNavigationButton("Ausgabe", () => NavigateToTab("Ausgabe")));
        quickStack.Children.Add(quick);
        quickCard.Child = quickStack;
        root.Children.Add(quickCard);

        tab.Content = scroll;
        _tabsByName["Übersicht"] = tab;
        MainTabs.Items.Add(tab);
    }

    private static Border CreateDashboardStatCard(string label, TextBlock value, string description)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(15),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)) });
        value.Foreground = new SolidColorBrush(Color.FromRgb(253, 186, 116));
        value.Margin = new Thickness(0, 5, 0, 3);
        stack.Children.Add(value);
        stack.Children.Add(new TextBlock { Text = description, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(139, 150, 166)), TextWrapping = TextWrapping.Wrap });
        card.Child = stack;
        return card;
    }

    private static Border CreateWorkflowCard(string number, string title, string description)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 25)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(39, 46, 58)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var stack = new StackPanel();
        stack.Children.Add(new Border
        {
            Width = 28,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromRgb(44, 26, 12)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(154, 79, 24)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new TextBlock { Text = number, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(253, 186, 116)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 3) });
        stack.Children.Add(new TextBlock { Text = description, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)), TextWrapping = TextWrapping.Wrap });
        card.Child = stack;
        return card;
    }

    private Button CreateQuickNavigationButton(string label, Action action, bool primary = false)
    {
        var button = new Button
        {
            Content = label,
            Style = TryFindResource(primary ? "PrimaryButtonStyle" : "GhostButtonStyle") as Style,
            Padding = new Thickness(14, 8, 14, 8)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void NavigateToTab(string tabName)
    {
        if (_tabsByName.TryGetValue(tabName, out var tab))
            MainTabs.SelectedItem = tab;
    }

    private void OpenModule(string tabName, string moduleName)
    {
        NavigateToTab(tabName);
        if (_moduleChecks.TryGetValue(moduleName, out var check)) check.IsChecked = true;
        if (_moduleExpanders.TryGetValue(moduleName, out var expander)) expander.IsExpanded = true;
        ApplyFilters();
        if (_moduleCards.TryGetValue(moduleName, out var card)) card.BringIntoView();
    }

    private void BuildModuleTabs()
    {
        var groups = ModuleCatalog.All
            .GroupBy(m => m.Tab)
            .OrderBy(g => Array.IndexOf(ModuleTabOrder, g.Key));

        foreach (var group in groups)
        {
            var modules = OrderModules(group.Key, group).ToList();
            _modulesByTab[group.Key] = modules;

            var tab = new TabItem { Header = $"{IconForTab(group.Key)}  {DisplayNameForTab(group.Key)}" };
            var grid = new Grid { Background = Brushes.Transparent };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(292) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = CreateLeftPanel(group.Key, modules);
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var splitter = new GridSplitter
            {
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(39, 46, 58)),
                ShowsPreview = true,
                Cursor = Cursors.SizeWE
            };
            Grid.SetColumn(splitter, 1);
            grid.Children.Add(splitter);

            var contentRoot = new Grid { Margin = new Thickness(10, 0, 0, 0) };
            contentRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var overview = CreateTabOverview(group.Key, modules);
            Grid.SetRow(overview, 0);
            contentRoot.Children.Add(overview);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var stack = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            scroll.Content = stack;
            Grid.SetRow(scroll, 1);
            contentRoot.Children.Add(scroll);

            var emptyState = CreateEmptyState(group.Key);
            _tabEmptyStates[group.Key] = emptyState;
            stack.Children.Add(emptyState);

            foreach (var module in modules)
                stack.Children.Add(CreateModuleCard(module));

            Grid.SetColumn(contentRoot, 2);
            grid.Children.Add(contentRoot);

            tab.Content = grid;
            _tabsByName[group.Key] = tab;
            MainTabs.Items.Add(tab);
        }
    }

    private FrameworkElement CreateEmptyState(string tabName)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 53, 66)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(28),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = IconForTab(tabName),
            FontSize = 30,
            Foreground = new SolidColorBrush(GetModuleColor(tabName)),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Noch kein Modul aktiviert",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 4)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Aktiviere links ein Modul. Die zugehörigen Eingaben erscheinen anschließend hier.",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520
        });
        card.Child = stack;
        return card;
    }

    private FrameworkElement CreateTabOverview(string tabName, List<ModuleDefinition> modules)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 11, 12, 11)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new Border
        {
            Background = new SolidColorBrush(GetModuleColor(tabName)),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(0, 1, 12, 1)
        });

        var text = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
        text.Children.Add(new TextBlock
        {
            Text = DisplayNameForTab(tabName),
            FontSize = 20,
            FontWeight = FontWeights.Bold
        });
        text.Children.Add(new TextBlock
        {
            Text = DescriptionForTab(tabName),
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var countText = new TextBlock
        {
            Text = "0 aktiv",
            Foreground = new SolidColorBrush(Color.FromRgb(253, 186, 116)),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var countBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(44, 26, 12)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(154, 79, 24)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 4, 9, 4),
            Margin = new Thickness(0, 0, 8, 0),
            Child = countText
        };
        _tabActiveCountTexts[tabName] = countText;
        actions.Children.Add(countBadge);

        var open = new Button { Content = "Alle öffnen", Style = TryFindResource("SmallButtonStyle") as Style };
        var close = new Button { Content = "Alle schließen", Style = TryFindResource("SmallButtonStyle") as Style };
        open.Click += (_, _) =>
        {
            foreach (var module in modules.Where(m => _moduleChecks.TryGetValue(m.Name, out var cb) && cb.IsChecked == true))
                if (_moduleExpanders.TryGetValue(module.Name, out var expander)) expander.IsExpanded = true;
        };
        close.Click += (_, _) =>
        {
            foreach (var module in modules)
                if (_moduleExpanders.TryGetValue(module.Name, out var expander)) expander.IsExpanded = false;
        };
        actions.Children.Add(open);
        actions.Children.Add(close);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        card.Child = grid;
        return card;
    }


    private Border CreateHelpIcon(ToolTip tip, string label = "Hilfe")
    {
        var icon = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromRgb(44, 26, 12)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(232, 121, 26)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(6, 0, 6, 0),
            ToolTip = tip,
            Cursor = Cursors.Help,
            Child = new TextBlock
            {
                Text = "?",
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(253, 186, 116)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        ToolTipService.SetInitialShowDelay(icon, 180);
        ToolTipService.SetBetweenShowDelay(icon, 80);
        ToolTipService.SetShowDuration(icon, 120000);
        icon.SetValue(AutomationProperties.NameProperty, label);
        return icon;
    }

    private Border CreateLeftPanel(string tabName, List<ModuleDefinition> modules)
    {
        var root = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 18, 24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(39, 46, 58)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12)
        };

        var dock = new DockPanel();
        root.Child = dock;

        var headerGrid = new Grid { Margin = new Thickness(2, 0, 2, 12) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Module", FontSize = 17, FontWeight = FontWeights.Bold },
                new TextBlock { Text = "Auswählen und konfigurieren", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(139, 150, 166)), Margin = new Thickness(0, 2, 0, 0) }
            }
        });
        var moduleCount = new TextBlock
        {
            Text = $"0 / {modules.Count}",
            Foreground = new SolidColorBrush(Color.FromRgb(253, 186, 116)),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var countBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(44, 26, 12)),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8, 3, 8, 3),
            Child = moduleCount
        };
        _tabModuleCountTexts[tabName] = moduleCount;
        Grid.SetColumn(countBadge, 1);
        headerGrid.Children.Add(countBadge);
        DockPanel.SetDock(headerGrid, Dock.Top);
        dock.Children.Add(headerGrid);

        var searchLabel = new TextBlock
        {
            Text = "Module durchsuchen",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            FontSize = 11,
            Margin = new Thickness(2, 0, 0, 4)
        };
        DockPanel.SetDock(searchLabel, Dock.Top);
        dock.Children.Add(searchLabel);

        var searchBox = new TextBox
        {
            MinHeight = 34,
            Margin = new Thickness(0, 0, 0, 9),
            ToolTip = TooltipBuilder.Create("Modulsuche", "Durchsucht die Module dieses Bereichs.\n\nGesucht wird in Modulname, interner Modul-ID, Feldnamen und Hilfetexten.")
        };
        searchBox.TextChanged += (_, _) => { _tabSearchText[tabName] = searchBox.Text ?? string.Empty; ApplyFilters(); };
        DockPanel.SetDock(searchBox, Dock.Top);
        dock.Children.Add(searchBox);

        var buttons = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 10) };
        var all = new Button { Content = "Alle", Style = TryFindResource("SmallButtonStyle") as Style, ToolTip = "Aktiviert alle sichtbaren Module." };
        var none = new Button { Content = "Keine", Style = TryFindResource("SmallButtonStyle") as Style, ToolTip = "Deaktiviert alle Module dieses Bereichs." };
        var favOnly = new Button { Content = "Favoriten", Style = TryFindResource("SmallButtonStyle") as Style, ToolTip = "Aktiviert nur die markierten Favoritenmodule." };
        all.Click += (_, _) => SetTabChecks(tabName, true);
        none.Click += (_, _) => SetTabChecks(tabName, false);
        favOnly.Click += (_, _) => ApplyModulePreset("Favoriten");
        buttons.Children.Add(all);
        buttons.Children.Add(none);
        buttons.Children.Add(favOnly);
        DockPanel.SetDock(buttons, Dock.Top);
        dock.Children.Add(buttons);

        var separator = new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(39, 46, 58)), Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(separator, Dock.Top);
        dock.Children.Add(separator);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var list = new StackPanel();
        scroll.Content = list;
        dock.Children.Add(scroll);

        foreach (var module in modules)
        {
            var check = new CheckBox
            {
                Content = module.Title,
                IsChecked = module.Default,
                Tag = module.Name,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"Modul aktivieren: {module.Title}"
            };
            check.Checked += (_, _) =>
            {
                InvalidateGeneratedState();
                ApplyFilters();
                UpdateConditionalFieldVisibility();
                if (_moduleExpanders.TryGetValue(module.Name, out var expander)) expander.IsExpanded = true;
                if (_moduleCards.TryGetValue(module.Name, out var card)) card.BringIntoView();
                if (module.Name.Equals("stpExtended", StringComparison.OrdinalIgnoreCase)) RefreshStpPreview();
            };
            check.Unchecked += (_, _) =>
            {
                InvalidateGeneratedState();
                ApplyFilters();
                UpdateConditionalFieldVisibility();
                if (module.Name.Equals("stpExtended", StringComparison.OrdinalIgnoreCase)) RefreshStpPreview();
            };

            var fav = new Button
            {
                Content = "☆",
                Width = 28,
                MinHeight = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(3, 0, 0, 0),
                Style = TryFindResource("SmallButtonStyle") as Style,
                ToolTip = "Als Favorit markieren oder entfernen."
            };
            fav.Click += (_, _) =>
            {
                if (_favoriteModules.Contains(module.Name))
                {
                    _favoriteModules.Remove(module.Name);
                    fav.Content = "☆";
                }
                else
                {
                    _favoriteModules.Add(module.Name);
                    fav.Content = "★";
                }
                ApplyFilters();
            };

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var marker = new Border
            {
                Background = new SolidColorBrush(GetModuleColor(module.Tab)),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 4, 8, 4),
                Opacity = 0.8
            };
            rowGrid.Children.Add(marker);
            Grid.SetColumn(check, 1);
            rowGrid.Children.Add(check);

            var moduleListHelp = CreateHelpIcon(TooltipBuilder.CreateModuleTip(module), "Modulhilfe");
            moduleListHelp.Margin = new Thickness(4, 0, 2, 0);
            Grid.SetColumn(moduleListHelp, 2);
            rowGrid.Children.Add(moduleListHelp);

            Grid.SetColumn(fav, 3);
            rowGrid.Children.Add(fav);

            var row = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(7, 6, 6, 6),
                Margin = new Thickness(0, 0, 0, 4),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(31, 37, 47)),
                BorderThickness = new Thickness(1),
                Child = rowGrid
            };
            row.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount >= 2 && _moduleCards.TryGetValue(module.Name, out var card))
                {
                    check.IsChecked = true;
                    card.BringIntoView();
                }
            };
            _moduleChecks[module.Name] = check;
            _moduleRows[module.Name] = row;
            list.Children.Add(row);
        }

        return root;
    }

    private Border CreateModuleCard(ModuleDefinition module)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(23, 28, 37)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 53, 66)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 10)
        };
        ToolTipService.SetIsEnabled(card, false);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var accent = new Border
        {
            Background = new SolidColorBrush(GetModuleColor(module.Tab)),
            CornerRadius = new CornerRadius(10, 0, 0, 10),
            Opacity = 0.9
        };
        Grid.SetColumn(accent, 0);
        layout.Children.Add(accent);

        var expander = new Expander
        {
            IsExpanded = module.Default,
            Header = CreateModuleHeader(module),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(13, 10, 13, 12)
        };
        _moduleExpanders[module.Name] = expander;

        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var field in module.Fields)
            panel.Children.Add(CreateField(field));
        expander.Content = panel;

        Grid.SetColumn(expander, 1);
        layout.Children.Add(expander);
        card.Child = layout;

        _moduleCards[module.Name] = card;
        return card;
    }

    private UIElement CreateModuleHeader(ModuleDefinition module)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titlePanel.Children.Add(new TextBlock
        {
            Text = module.Title,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        titlePanel.Children.Add(CreateHelpIcon(TooltipBuilder.CreateModuleTip(module), "Modulhilfe / Befehle / Befehlsteile"));
        grid.Children.Add(titlePanel);

        var badges = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };

        badges.Children.Add(CreateBadge(string.Join(" / ", module.Devices), Color.FromRgb(31, 37, 47), Color.FromRgb(156, 166, 181)));
        badges.Children.Add(CreateBadge($"{module.Fields.Count} Felder", Color.FromRgb(44, 26, 12), Color.FromRgb(253, 186, 116)));

        var statusText = new TextBlock
        {
            Text = "Aus",
            FontSize = 11,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        var statusBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(5, 0, 0, 0),
            ToolTip = "Validierungsstatus dieses Moduls.",
            Child = statusText
        };
        _moduleStatusBadges[module.Name] = statusBadge;
        _moduleStatusTexts[module.Name] = statusText;
        badges.Children.Add(statusBadge);

        Grid.SetColumn(badges, 1);
        grid.Children.Add(badges);
        return grid;
    }

    private static Border CreateBadge(string text, Color background, Color foreground)
    {
        return new Border
        {
            Background = new SolidColorBrush(background),
            BorderBrush = new SolidColorBrush(Color.FromRgb(58, 67, 81)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(5, 0, 0, 0),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = new SolidColorBrush(foreground),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private FrameworkElement CreateField(FieldDefinition field)
    {
        var wrapper = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 23, 31)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(39, 46, 58)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8)
        };
        _fieldWrappers[field.Name] = wrapper;
        _fieldDefinitions[field.Name] = field;

        var isPreview = string.Equals(field.Type, "Preview", StringComparison.OrdinalIgnoreCase);
        var isMulti = string.Equals(field.Type, "Multi", StringComparison.OrdinalIgnoreCase) || isPreview;

        Control control;
        if (string.Equals(field.Type, "Combo", StringComparison.OrdinalIgnoreCase))
        {
            var combo = new ComboBox { ItemsSource = field.Items, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SelectedIndex = Math.Clamp(field.Selected, 0, Math.Max(0, field.Items.Count - 1));
            control = combo;
        }
        else
        {
            var text = new TextBox
            {
                Text = field.Value,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                IsReadOnly = field.ReadOnly || isPreview,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            if (isMulti)
            {
                text.AcceptsReturn = true;
                text.AcceptsTab = true;
                text.VerticalContentAlignment = VerticalAlignment.Top;
                text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                text.FontFamily = new FontFamily("Consolas");
                text.MinHeight = Math.Max(96, field.H + 18);
            }

            if (isPreview)
            {
                text.Background = new SolidColorBrush(Color.FromRgb(8, 11, 16));
                text.Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240));
                text.Text = "STP-Modul aktivieren, um die Live-Vorschau anzuzeigen.";
                _previewControls[field.Name] = text;
            }

            control = text;
        }

        var label = new TextBlock
        {
            Text = field.Label,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        var fieldHelp = CreateHelpIcon(TooltipBuilder.CreateFieldTip(field), "Feldhilfe / Syntax / Befehlsteile");
        fieldHelp.Margin = new Thickness(6, 0, 0, 0);

        if (isMulti)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            DockPanel.SetDock(fieldHelp, Dock.Right);
            header.Children.Add(fieldHelp);
            header.Children.Add(label);
            grid.Children.Add(header);
            Grid.SetRow(control, 1);
            grid.Children.Add(control);
            wrapper.Child = grid;
        }
        else
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(235) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            label.Margin = new Thickness(0, 0, 10, 0);
            grid.Children.Add(label);
            Grid.SetColumn(fieldHelp, 1);
            grid.Children.Add(fieldHelp);
            Grid.SetColumn(control, 2);
            grid.Children.Add(control);
            wrapper.Child = grid;
        }

        if (!isPreview)
        {
            if (control is TextBox textControl)
                textControl.TextChanged += (_, _) => OnFieldValueChanged(field.Name);
            else if (control is ComboBox comboControl)
                comboControl.SelectionChanged += (_, _) => OnFieldValueChanged(field.Name);
            else if (control is CheckBox checkControl)
            {
                checkControl.Checked += (_, _) => OnFieldValueChanged(field.Name);
                checkControl.Unchecked += (_, _) => OnFieldValueChanged(field.Name);
            }
            _valueControls[field.Name] = control;
        }

        return wrapper;
    }

    private void OnFieldValueChanged(string fieldName)
    {
        InvalidateGeneratedState();
        UpdateConditionalFieldVisibility();
        UpdateModuleStatuses();
        UpdateStatusBar();

        if (fieldName.StartsWith("stp", StringComparison.OrdinalIgnoreCase) ||
            fieldName.Equals("deviceType", StringComparison.OrdinalIgnoreCase))
            RefreshStpPreview();
    }

    private void UpdateConditionalFieldVisibility()
    {
        foreach (var (name, definition) in _fieldDefinitions)
        {
            if (!_fieldWrappers.TryGetValue(name, out var wrapper)) continue;

            var dependencyValue = string.IsNullOrWhiteSpace(definition.DependsOnField)
                ? string.Empty
                : GetFieldValue(definition.DependsOnField);

            var visible = definition.VisibleForValues.Count == 0 ||
                          definition.VisibleForValues.Any(x => string.Equals(x, dependencyValue, StringComparison.OrdinalIgnoreCase));
            var enabled = definition.EnabledForValues.Count == 0 ||
                          definition.EnabledForValues.Any(x => string.Equals(x, dependencyValue, StringComparison.OrdinalIgnoreCase));

            wrapper.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            wrapper.IsEnabled = enabled;
        }
    }

    private bool IsFieldApplicable(FieldDefinition field)
    {
        if (field.VisibleForValues.Count == 0) return true;
        if (string.IsNullOrWhiteSpace(field.DependsOnField)) return true;
        var dependencyValue = GetFieldValue(field.DependsOnField);
        return field.VisibleForValues.Any(x => string.Equals(x, dependencyValue, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshStpPreview()
    {
        _ = RefreshStpPreviewAsync();
    }

    private async Task RefreshStpPreviewAsync()
    {
        if (_updatingStpPreview || !_previewControls.TryGetValue("stpLivePreview", out var preview)) return;

        _updatingStpPreview = true;
        try
        {
            if (!_moduleChecks.TryGetValue("stpExtended", out var moduleCheck) || moduleCheck.IsChecked != true)
            {
                preview.Text = "STP-Modul aktivieren, um die Live-Vorschau anzuzeigen.";
                return;
            }

            var config = await NativeCiscoGenerator.GenerateAsync(BuildRequest());
            preview.Text = ExtractGeneratedSection(config, "STP ERWEITERT");
            preview.ScrollToHome();
        }
        catch (Exception ex)
        {
            preview.Text = "Vorschau konnte nicht erzeugt werden: " + ex.Message;
        }
        finally
        {
            _updatingStpPreview = false;
        }
    }

    private static string ExtractGeneratedSection(string config, string sectionTitle)
    {
        var lines = config.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var titleIndex = Array.FindIndex(lines, x => x.Trim().Equals("! " + sectionTitle, StringComparison.OrdinalIgnoreCase));
        if (titleIndex < 0) return "Für die aktuellen Angaben wurden keine STP-Befehle erzeugt.";

        var start = Math.Max(0, titleIndex - 2);
        var end = lines.Length;
        for (var i = titleIndex + 2; i < lines.Length - 1; i++)
        {
            if (lines[i].Trim() == "!" && lines[i + 1].Trim().StartsWith("! ===", StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }

        return string.Join(Environment.NewLine, lines[start..end]).Trim();
    }

    private void BuildConfigurationTab()
    {
        var tab = new TabItem { Header = "📄  Ausgabe" };
        var root = new Grid { Margin = new Thickness(0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var top = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var topGrid = new Grid();
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Child = topGrid;

        var info = new StackPanel();
        info.Children.Add(new TextBlock
        {
            Text = "Konfiguration erzeugen",
            FontSize = 19,
            FontWeight = FontWeights.Bold
        });
        info.Children.Add(new TextBlock
        {
            Text = "Aktive Module zusammenführen, prüfen, kopieren oder direkt über die serielle Konsole übertragen.",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            FontSize = 12,
            Margin = new Thickness(0, 3, 0, 0)
        });
        topGrid.Children.Add(info);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var refreshButton = new Button
        {
            Content = "Vorschau aktualisieren",
            Style = TryFindResource("PrimaryButtonStyle") as Style,
            ToolTip = TooltipBuilder.Create("Vorschau aktualisieren", "Erzeugt die Cisco-Konfiguration anhand der aktuell gesetzten Werte und Module.\n\nDie Vorschau wird nicht automatisch dauerhaft unter der Eingabemaske angezeigt.")
        };
        refreshButton.Click += async (_, _) => await RefreshConfigurationPreviewAsync();

        var copyButton = new Button
        {
            Content = "Vorschau kopieren",
            ToolTip = TooltipBuilder.Create("Vorschau kopieren", "Kopiert den Inhalt des Vorschau-Feldes in die Zwischenablage.\n\nIst die Vorschau leer, wird sie vorher neu erzeugt.")
        };
        copyButton.Click += async (_, _) => await CopyPreviewAsync();

        var exportButton = new Button
        {
            Content = "TXT Export",
            ToolTip = TooltipBuilder.Create("TXT Export", "Erzeugt die Konfiguration im Hintergrund und speichert sie als Textdatei.")
        };
        exportButton.Click += async (_, _) => await SaveTextExportAsync();

        var peerButton = new Button
        {
            Content = "Gegenstelle anzeigen",
            ToolTip = TooltipBuilder.Create("Gegenstelle anzeigen", "Öffnet die automatisch abgeleiteten Anforderungen und Beispielbefehle für das jeweils andere Gerät.")
        };
        peerButton.Click += (_, _) =>
        {
            NavigateToTab("Gegenstelle");
            RefreshPeerRequirements();
        };

        var comMenuButton = new Button
        {
            Content = "COM / Konsole",
            ToolTip = TooltipBuilder.Create("COM / Konsole", "Fasst die serielle Ausgabe zusammen.\n\nOptionen:\nCOM-Port wählen\nCOM-Testmodus aktivieren\nKonfiguration an COM senden\n\nDer Testmodus benötigt keinen echten COM-Port und schreibt die Ausgabe in eine Logdatei.")
        };

        var comMenu = new ContextMenu();
        var comSelectItem = new MenuItem { Header = "COM-Port wählen" };
        comSelectItem.Click += (_, _) => SelectComPort();

        var comTestItem = new MenuItem { Header = "COM-Testmodus aktivieren" };
        comTestItem.Click += (_, _) => EnableComTestMode();

        var comSendItem = new MenuItem { Header = "Konfiguration senden" };
        comSendItem.Click += async (_, _) => await SendConfigToComPortAsync();

        comMenu.Items.Add(comSelectItem);
        comMenu.Items.Add(comTestItem);
        comMenu.Items.Add(new Separator());
        comMenu.Items.Add(comSendItem);

        comMenuButton.ContextMenu = comMenu;
        comMenuButton.Click += (_, _) =>
        {
            comMenu.PlacementTarget = comMenuButton;
            comMenu.IsOpen = true;
        };

        buttons.Children.Add(refreshButton);
        buttons.Children.Add(copyButton);
        buttons.Children.Add(exportButton);
        buttons.Children.Add(peerButton);
        buttons.Children.Add(comMenuButton);
        Grid.SetColumn(buttons, 1);
        topGrid.Children.Add(buttons);

        _configurationPreviewBox = new RichTextBox
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            IsReadOnly = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 25)),
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(229, 231, 235))
        };
        SetConfigurationPreviewText("Klicke auf 'Vorschau aktualisieren', um die aktuelle Cisco-Konfiguration zu erzeugen.");

        var previewBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 25)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = _configurationPreviewBox
        };

        Grid.SetRow(top, 0);
        Grid.SetRow(previewBorder, 1);
        root.Children.Add(top);
        root.Children.Add(previewBorder);

        tab.Content = root;
        _tabsByName["Ausgabe"] = tab;
        MainTabs.Items.Add(tab);

        MainTabs.SelectionChanged += async (_, e) =>
        {
            if (!ReferenceEquals(e.Source, MainTabs)) return;
            if (!ReferenceEquals(MainTabs.SelectedItem, tab)) return;

            if (_configurationPreviewBox != null &&
                (_configurationPreviewText.StartsWith("Klicke auf", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(_configurationPreviewText)))
            {
                await RefreshConfigurationPreviewAsync();
            }
        };
    }

    private async Task RefreshConfigurationPreviewAsync()
    {
        if (_configurationPreviewBox == null) return;

        try
        {
            SetConfigurationPreviewText("Konfiguration wird erzeugt ...");
            var config = await GenerateConfigAsync();
            SetConfigurationPreviewText(AddDuplicateReportToPreview(config));
        }
        catch (Exception ex)
        {
            SetConfigurationPreviewText("Fehler beim Erzeugen der Konfiguration:\n" + ex.Message);
        }
    }

    private async Task CopyPreviewAsync()
    {
        if (_configurationPreviewBox == null) return;

        if (string.IsNullOrWhiteSpace(_configurationPreviewText) ||
            _configurationPreviewText.StartsWith("Klicke auf", StringComparison.OrdinalIgnoreCase) ||
            _configurationPreviewText.StartsWith("Konfiguration wird", StringComparison.OrdinalIgnoreCase))
        {
            await RefreshConfigurationPreviewAsync();
        }

        Clipboard.SetText(_configurationPreviewText ?? string.Empty);
        MessageBox.Show(this, "Vorschau wurde in die Zwischenablage kopiert.", "Kopiert", MessageBoxButton.OK, MessageBoxImage.Information);
    }



    private void BuildPeerRequirementsTab()
    {
        var tab = new TabItem { Header = "⇄  Gegenstelle" };
        var root = new Grid { Margin = new Thickness(0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var top = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var topGrid = new Grid();
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel();
        info.Children.Add(new TextBlock
        {
            Text = "Anforderungen der Gegenstelle",
            FontSize = 19,
            FontWeight = FontWeights.Bold
        });
        info.Children.Add(new TextBlock
        {
            Text = "Zeigt zu aktiven Trunks, EtherChannels und Routing-/VPN-Protokollen die erforderlichen Parameter und Beispielbefehle für das Nachbargerät.",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            FontSize = 12,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        topGrid.Children.Add(info);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        var refreshButton = new Button
        {
            Content = "Gegenstelle aktualisieren",
            Style = TryFindResource("PrimaryButtonStyle") as Style,
            ToolTip = "Leitet die Anforderungen erneut aus allen aktuellen Eingaben und aktiven Modulen ab."
        };
        refreshButton.Click += (_, _) => RefreshPeerRequirements();

        var copyButton = new Button
        {
            Content = "Kopieren",
            ToolTip = "Kopiert die Gegenstellenanforderungen in die Zwischenablage."
        };
        copyButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_peerRequirementsText)) RefreshPeerRequirements();
            Clipboard.SetText(_peerRequirementsText);
            MessageBox.Show(this, "Gegenstellenanforderungen wurden kopiert.", "Kopiert", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        var saveButton = new Button
        {
            Content = "TXT Export",
            ToolTip = "Speichert die Gegenstellenanforderungen als Textdatei."
        };
        saveButton.Click += (_, _) => SavePeerRequirements();

        actions.Children.Add(refreshButton);
        actions.Children.Add(copyButton);
        actions.Children.Add(saveButton);
        Grid.SetColumn(actions, 1);
        topGrid.Children.Add(actions);
        top.Child = topGrid;

        _peerRequirementsBox = new TextBox
        {
            Text = "Klicke auf 'Gegenstelle aktualisieren', um die Anforderungen des Nachbargeräts zu erzeugen.",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 25)),
            Foreground = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
            BorderThickness = new Thickness(0)
        };

        var outputBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 25)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = _peerRequirementsBox
        };

        Grid.SetRow(top, 0);
        Grid.SetRow(outputBorder, 1);
        root.Children.Add(top);
        root.Children.Add(outputBorder);
        tab.Content = root;
        _tabsByName["Gegenstelle"] = tab;
        MainTabs.Items.Add(tab);

        MainTabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, MainTabs)) return;
            if (!ReferenceEquals(MainTabs.SelectedItem, tab)) return;
            if (string.IsNullOrWhiteSpace(_peerRequirementsText)) RefreshPeerRequirements();
        };
    }

    private void RefreshPeerRequirements()
    {
        if (_peerRequirementsBox == null) return;
        try
        {
            _peerRequirementsText = PeerRequirementGenerator.Generate(BuildRequest());
            _peerRequirementsBox.Text = _peerRequirementsText;
            _peerRequirementsBox.ScrollToHome();
        }
        catch (Exception ex)
        {
            _peerRequirementsText = "Gegenstellenanforderungen konnten nicht erzeugt werden:\n" + ex.Message;
            _peerRequirementsBox.Text = _peerRequirementsText;
        }
    }

    private void SavePeerRequirements()
    {
        if (string.IsNullOrWhiteSpace(_peerRequirementsText)) RefreshPeerRequirements();
        var dialog = new SaveFileDialog
        {
            Title = "Gegenstellenanforderungen speichern",
            Filter = "Textdatei (*.txt)|*.txt|Cisco-Konfiguration (*.cfg)|*.cfg|Alle Dateien (*.*)|*.*",
            FileName = "gegenstelle_anforderungen.txt"
        };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, _peerRequirementsText, new UTF8Encoding(false));
    }

    private void BuildImportConfigTab()
    {
        var tab = new TabItem { Header = "📥  Import" };
        var root = new Grid { Margin = new Thickness(0) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new Grid { Margin = new Thickness(0, 0, 8, 0) };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var right = new Grid { Margin = new Thickness(8, 0, 0, 0) };
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var importInfo = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var importInfoStack = new StackPanel();
        importInfoStack.Children.Add(new TextBlock { Text = "Cisco-Konfiguration importieren", FontSize = 19, FontWeight = FontWeights.Bold });
        importInfoStack.Children.Add(new TextBlock
        {
            Text = "Komplette Running-Config oder Teilkonfiguration einfügen. Bekannte Werte werden übernommen; nicht unterstützte Befehle musst du nicht löschen, sie werden separat notiert.",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });
        importInfo.Child = importInfoStack;
        Grid.SetRow(importInfo, 0);
        left.Children.Add(importInfo);

        var importButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var loadButton = new Button { Content = "Config-Datei laden", ToolTip = "Lädt eine vorhandene TXT/CFG-Konfiguration in das Importfeld." };
        var analyzeButton = new Button { Content = "Analysieren", Style = TryFindResource("PrimaryButtonStyle") as Style, ToolTip = "Analysiert komplette Konfigurationen. Bekannte Werte werden erkannt, unbekannte Befehle werden mit Kontext notiert." };
        var applyButton = new Button { Content = "Daten übernehmen", Style = TryFindResource("PrimaryButtonStyle") as Style, ToolTip = "Übernimmt erkannte Werte in die vorhandenen Module und aktiviert passende Module." };
        var exportUnknownButton = new Button { Content = "Unbekannte exportieren", ToolTip = "Exportiert alle nicht zugeordneten Befehle als Textdatei." };

        loadButton.Click += (_, _) => LoadConfigForImport();
        analyzeButton.Click += (_, _) => AnalyzeImportedConfig();
        applyButton.Click += (_, _) => ApplyImportedConfig();
        exportUnknownButton.Click += (_, _) => ExportUnknownImportedCommands();

        importButtons.Children.Add(loadButton);
        importButtons.Children.Add(analyzeButton);
        importButtons.Children.Add(applyButton);
        importButtons.Children.Add(exportUnknownButton);
        Grid.SetRow(importButtons, 1);
        left.Children.Add(importButtons);

        _importConfigBox = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Text = "! running-config hier einfügen\nhostname R1\ninterface GigabitEthernet0/0\n ip address 192.168.1.1 255.255.255.0\n no shutdown\nrouter ospf 1\n network 192.168.1.0 0.0.0.255 area 0"
        };
        var importBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 25)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = _importConfigBox
        };
        Grid.SetRow(importBorder, 2);
        left.Children.Add(importBorder);

        var resultInfo = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var resultInfoStack = new StackPanel();
        resultInfoStack.Children.Add(new TextBlock { Text = "Import-Ergebnis", FontSize = 19, FontWeight = FontWeights.Bold });
        resultInfoStack.Children.Add(new TextBlock
        {
            Text = "Rechts stehen Zusammenfassung, übernehmbare Felder, aktivierte Module und unbekannte Befehle.",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });
        resultInfo.Child = resultInfoStack;
        Grid.SetRow(resultInfo, 0);
        right.Children.Add(resultInfo);

        var resultButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var copyUnknownButton = new Button { Content = "Unbekannte kopieren", ToolTip = "Kopiert die unbekannten Befehle in die Zwischenablage." };
        var copySummaryButton = new Button { Content = "Zusammenfassung kopieren", ToolTip = "Kopiert das gesamte Import-Ergebnis in die Zwischenablage." };
        copyUnknownButton.Click += (_, _) => CopyUnknownImportedCommands();
        copySummaryButton.Click += (_, _) => Clipboard.SetText(_importResultBox?.Text ?? string.Empty);
        resultButtons.Children.Add(copyUnknownButton);
        resultButtons.Children.Add(copySummaryButton);
        Grid.SetRow(resultButtons, 1);
        right.Children.Add(resultButtons);

        _importResultBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Text = "Noch keine Analyse ausgeführt."
        };
        var resultBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 25)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Child = _importResultBox
        };
        Grid.SetRow(resultBorder, 2);
        right.Children.Add(resultBorder);

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        root.Children.Add(left);
        root.Children.Add(right);

        tab.Content = root;
        _tabsByName["Import"] = tab;
        MainTabs.Items.Add(tab);
    }

    private void LoadConfigForImport()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Cisco-Konfiguration laden",
            Filter = "Cisco/Text (*.txt;*.cfg;*.conf)|*.txt;*.cfg;*.conf|Alle Dateien (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        if (_importConfigBox == null) return;

        _importConfigBox.Text = File.ReadAllText(dialog.FileName, Encoding.UTF8);
        AnalyzeImportedConfig();
    }

    private void AnalyzeImportedConfig()
    {
        if (_importConfigBox == null || _importResultBox == null) return;
        _lastImportAnalysis = ImportedConfigAnalyzer.Analyze(_importConfigBox.Text);
        _importResultBox.Text = FormatImportAnalysis(_lastImportAnalysis);
        ValidationTextBlock.Text = _lastImportAnalysis.UnknownCommands.Count == 0
            ? $"Importanalyse: {_lastImportAnalysis.AppliedFields} Felder erkannt · keine unbekannten Befehle"
            : $"Importanalyse: {_lastImportAnalysis.AppliedFields} Felder erkannt · {_lastImportAnalysis.UnknownCommands.Count} unbekannte Befehle";
        ValidationTextBlock.Foreground = _lastImportAnalysis.UnknownCommands.Count == 0
            ? new SolidColorBrush(Color.FromRgb(134, 239, 172))
            : new SolidColorBrush(Color.FromRgb(251, 191, 36));
    }

    private void ApplyImportedConfig()
    {
        if (_lastImportAnalysis == null)
            AnalyzeImportedConfig();
        if (_lastImportAnalysis == null) return;

        ResetControlsForImportedModules(_lastImportAnalysis);

        foreach (var pair in _lastImportAnalysis.Values)
            SetControlValue(pair.Key, pair.Value);

        foreach (var check in _moduleChecks.Values)
            check.IsChecked = false;

        foreach (var pair in _lastImportAnalysis.Modules)
        {
            if (_moduleChecks.TryGetValue(pair.Key, out var check))
                check.IsChecked = pair.Value;
        }

        InvalidateGeneratedState();
        ApplyFilters();
        UpdateConditionalFieldVisibility();
        UpdateModuleStatuses();
        UpdateStatusBar();
        RefreshStpPreview();

        MessageBox.Show(this,
            $"Import übernommen.\n\nFelder: {_lastImportAnalysis.AppliedFields}\nModule aktiviert: {_lastImportAnalysis.ActiveModules}\nUnbekannte Befehle: {_lastImportAnalysis.UnknownCommands.Count}",
            "Import übernommen",
            MessageBoxButton.OK,
            _lastImportAnalysis.UnknownCommands.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void ResetControlsForImportedModules(ImportedConfigAnalysis analysis)
    {
        var activeModuleNames = analysis.Modules
            .Where(x => x.Value)
            .Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var module in ModuleCatalog.All.Where(x => activeModuleNames.Contains(x.Name)))
        {
            foreach (var field in module.Fields)
            {
                if (!_valueControls.TryGetValue(field.Name, out var control)) continue;

                if (control is TextBox textBox)
                {
                    textBox.Text = string.Empty;
                    continue;
                }

                if (control is ComboBox comboBox)
                {
                    var neutral = comboBox.Items.Cast<object>()
                        .FirstOrDefault(x => string.IsNullOrWhiteSpace(x?.ToString()))
                        ?? comboBox.Items.Cast<object>()
                            .FirstOrDefault(x => string.Equals(x?.ToString(), "Nein", StringComparison.OrdinalIgnoreCase))
                        ?? comboBox.Items.Cast<object>().FirstOrDefault();

                    comboBox.SelectedItem = neutral;
                }
            }
        }
    }

    private void ExportUnknownImportedCommands()
    {
        if (_lastImportAnalysis == null)
            AnalyzeImportedConfig();
        if (_lastImportAnalysis == null) return;

        var dialog = new SaveFileDialog
        {
            Title = "Unbekannte Importbefehle speichern",
            Filter = "Textdatei (*.txt)|*.txt|Alle Dateien (*.*)|*.*",
            FileName = "unbekannte_cisco_befehle.txt"
        };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, FormatUnknownImportedCommands(_lastImportAnalysis), new UTF8Encoding(false));
    }

    private void CopyUnknownImportedCommands()
    {
        if (_lastImportAnalysis == null)
            AnalyzeImportedConfig();
        if (_lastImportAnalysis == null) return;
        Clipboard.SetText(FormatUnknownImportedCommands(_lastImportAnalysis));
    }

    private string FormatImportAnalysis(ImportedConfigAnalysis analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("IMPORT-ZUSAMMENFASSUNG");
        sb.AppendLine("====================");
        sb.AppendLine($"Befehlszeilen gesamt     : {analysis.TotalCommands}");
        sb.AppendLine($"Bekannt / zugeordnet     : {analysis.KnownCommands}");
        sb.AppendLine($"Felder übernehmbar       : {analysis.AppliedFields}");
        sb.AppendLine($"Module aktivierbar       : {analysis.ActiveModules}");
        sb.AppendLine($"Unbekannte Befehle       : {analysis.UnknownCommands.Count}");
        sb.AppendLine();

        if (analysis.Notes.Count > 0)
        {
            sb.AppendLine("HINWEISE");
            sb.AppendLine("--------");
            foreach (var note in analysis.Notes)
                sb.AppendLine("- " + note);
            sb.AppendLine();
        }

        sb.AppendLine("ERKANNTE MODULE");
        sb.AppendLine("---------------");
        foreach (var moduleName in analysis.Modules.Where(x => x.Value).Select(x => x.Key).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var module = ModuleCatalog.All.FirstOrDefault(m => m.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
            sb.AppendLine("- " + (module?.Title ?? moduleName) + $" [{moduleName}]");
        }
        sb.AppendLine();

        sb.AppendLine("ÜBERNEHMBARE FELDER");
        sb.AppendLine("-------------------");
        foreach (var pair in analysis.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var preview = (pair.Value ?? string.Empty).Replace("\r\n", " ").Replace("\n", " ");
            if (preview.Length > 140) preview = preview[..140] + " ...";
            sb.AppendLine($"{pair.Key}: {preview}");
        }
        sb.AppendLine();

        sb.AppendLine("UNBEKANNTE BEFEHLE");
        sb.AppendLine("------------------");
        if (analysis.UnknownCommands.Count == 0)
        {
            sb.AppendLine("Keine unbekannten Befehle erkannt.");
        }
        else
        {
            foreach (var item in analysis.UnknownCommands.Take(300))
                sb.AppendLine($"Zeile {item.LineNumber,4} | {item.Context,-16} | {item.Command}");
            if (analysis.UnknownCommands.Count > 300)
                sb.AppendLine($"... weitere {analysis.UnknownCommands.Count - 300} Befehle ausgeblendet.");
        }
        return sb.ToString();
    }

    private static string FormatUnknownImportedCommands(ImportedConfigAnalysis analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Unbekannte Cisco-Befehle aus Import");
        sb.AppendLine("===================================");
        sb.AppendLine($"Anzahl: {analysis.UnknownCommands.Count}");
        sb.AppendLine();
        foreach (var item in analysis.UnknownCommands)
            sb.AppendLine($"Zeile {item.LineNumber,4} | Kontext: {item.Context} | {item.Command}");
        return sb.ToString();
    }

    private void SetControlValue(string name, string value)
    {
        if (name.Equals("deviceType", StringComparison.OrdinalIgnoreCase))
        {
            DeviceTypeCombo.SelectedItem = value;
            return;
        }
        if (name.Equals("configMode", StringComparison.OrdinalIgnoreCase))
        {
            ConfigModeCombo.SelectedItem = value;
            return;
        }
        if (name.Equals("writeMem", StringComparison.OrdinalIgnoreCase))
        {
            WriteMemCombo.SelectedItem = value;
            return;
        }

        if (!_valueControls.TryGetValue(name, out var control)) return;

        if (control is TextBox tb)
        {
            tb.Text = value ?? string.Empty;
            return;
        }

        if (control is ComboBox cb)
        {
            var match = cb.Items.Cast<object>().FirstOrDefault(x => string.Equals(x?.ToString(), value, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                cb.SelectedItem = match;
                return;
            }

            if (IsTruthy(value))
            {
                match = cb.Items.Cast<object>().FirstOrDefault(x => string.Equals(x?.ToString(), "Ja", StringComparison.OrdinalIgnoreCase));
                if (match != null) cb.SelectedItem = match;
                return;
            }

            if (IsFalsy(value))
            {
                match = cb.Items.Cast<object>().FirstOrDefault(x => string.Equals(x?.ToString(), "Nein", StringComparison.OrdinalIgnoreCase));
                if (match != null) cb.SelectedItem = match;
            }
        }
    }

    private static bool IsTruthy(string? value) =>
        value?.Equals("Ja", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Equals("yes", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Equals("on", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Equals("enabled", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsFalsy(string? value) =>
        value?.Equals("Nein", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Equals("false", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Equals("no", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Equals("off", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Equals("disabled", StringComparison.OrdinalIgnoreCase) == true;

    private void BuildCommandTab()
    {
        var tab = new TabItem { Header = "⌨  Befehlsregister" };
        var outer = new Grid { Margin = new Thickness(0) };
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var commandGroups = new List<CommandGroup>();
        commandGroups.AddRange(CommandCatalog.All);
        commandGroups.AddRange(ConfigurationCommandCatalog.All);

        var top = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var topGrid = new Grid();
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        top.Child = topGrid;

        var info = new StackPanel();
        info.Children.Add(new TextBlock
        {
            Text = "Befehlsregister",
            FontSize = 19,
            FontWeight = FontWeights.Bold
        });
        info.Children.Add(new TextBlock
        {
            Text = "Cisco-Syntax nach Bereich, Modul, Modus und Bedeutung durchsuchen.",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            FontSize = 12,
            Margin = new Thickness(0, 3, 0, 0)
        });
        topGrid.Children.Add(info);

        var groupPanel = new StackPanel();
        groupPanel.Children.Add(new TextBlock
        {
            Text = "Bereich",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var groupSelector = new ComboBox
        {
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ToolTip = TooltipBuilder.Create("Bereich auswählen", "Zeigt alle Befehle oder nur die Befehle einer bestimmten Gruppe an.")
        };
        groupSelector.Items.Add("Alle Bereiche");
        foreach (var group in commandGroups)
            groupSelector.Items.Add(group.Name);
        groupSelector.SelectedIndex = 0;
        groupPanel.Children.Add(groupSelector);
        Grid.SetColumn(groupPanel, 1);
        topGrid.Children.Add(groupPanel);

        var searchPanel = new StackPanel();
        searchPanel.Children.Add(new TextBlock
        {
            Text = "Suche",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var searchBox = new TextBox
        {
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            ToolTip = TooltipBuilder.Create("Suche", "Durchsucht Modul, Modus, Befehl und Beschreibung.\n\nBeispiele:\nOSPF\nVRF\ninterface\nssh\n<ACL>\nroute-map")
        };
        searchPanel.Children.Add(searchBox);
        Grid.SetColumn(searchPanel, 3);
        topGrid.Children.Add(searchPanel);

        var contentBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(1)
        };

        var contentGrid = new Grid();
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var resultBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 23, 32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8)
        };
        var resultText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(173, 182, 195)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };
        resultBar.Child = resultText;
        contentGrid.Children.Add(resultBar);

        var wrapStyle = new Style(typeof(TextBlock));
        wrapStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        wrapStyle.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(6, 4, 6, 4)));
        wrapStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));

        var commandGrid = new DataGrid
        {
            Margin = new Thickness(0),
            AlternationCount = 2,
            RowHeight = double.NaN,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true
        };

        commandGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Modul",
            Binding = new System.Windows.Data.Binding("Module"),
            Width = new DataGridLength(150),
            ElementStyle = wrapStyle
        });
        commandGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Modus",
            Binding = new System.Windows.Data.Binding("Mode"),
            Width = new DataGridLength(135),
            ElementStyle = wrapStyle
        });
        commandGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Befehl / Syntax",
            Binding = new System.Windows.Data.Binding("Command"),
            Width = new DataGridLength(430),
            ElementStyle = wrapStyle
        });
        commandGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Beschreibung",
            Binding = new System.Windows.Data.Binding("Meaning"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            ElementStyle = wrapStyle
        });

        Grid.SetRow(commandGrid, 1);
        contentGrid.Children.Add(commandGrid);
        contentBorder.Child = contentGrid;

        Grid.SetRow(top, 0);
        Grid.SetRow(contentBorder, 1);
        outer.Children.Add(top);
        outer.Children.Add(contentBorder);

        void ApplyCommandFilter()
        {
            IEnumerable<CommandRow> rows;
            var selectedIndex = groupSelector.SelectedIndex;
            if (selectedIndex <= 0)
                rows = commandGroups.SelectMany(group => group.Rows);
            else
                rows = commandGroups[selectedIndex - 1].Rows;

            var q = (searchBox.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(q))
            {
                rows = rows.Where(r =>
                    Contains(r.Module, q) ||
                    Contains(r.Mode, q) ||
                    Contains(r.Command, q) ||
                    Contains(r.Meaning, q));
            }

            var filtered = rows.ToList();
            commandGrid.ItemsSource = filtered;
            var selectedArea = groupSelector.SelectedItem?.ToString() ?? "Alle Bereiche";
            resultText.Text = $"{selectedArea} · {filtered.Count} Befehle";
        }

        static bool Contains(string source, string query) =>
            !string.IsNullOrEmpty(source) && source.Contains(query, StringComparison.OrdinalIgnoreCase);

        groupSelector.SelectionChanged += (_, _) => ApplyCommandFilter();
        searchBox.TextChanged += (_, _) => ApplyCommandFilter();
        ApplyCommandFilter();

        tab.Content = outer;
        _tabsByName["Befehlsregister"] = tab;
        MainTabs.Items.Add(tab);
    }

    private void BuildCheckTab()
    {
        var tab = new TabItem { Header = "▦  Subnetting" };
        var grid = new Grid
        {
            Margin = new Thickness(4),
            Background = new SolidColorBrush(Color.FromRgb(13, 16, 22))
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var inputCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18),
            BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
            BorderThickness = new Thickness(1)
        };

        var stack = new StackPanel();
        inputCard.Child = stack;

        stack.Children.Add(new TextBlock
        {
            Text = "Subnetting / Subnetzrechner",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Unterstützt IPv4 und IPv6 automatisch anhand der eingegebenen Adresse. Netz eingeben, Zielpräfix setzen und rechts die berechneten Subnetze prüfen.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(174, 184, 197)),
            Margin = new Thickness(0, 0, 0, 14)
        });

        var network = AddCheckField(stack, "Netz / CIDR (IPv4 oder IPv6)", "192.168.10.0/24");
        var newPrefix = AddCheckField(stack, "Neuer Präfix", "26");
        var maxSubnets = AddCheckField(stack, "Max. Subnetze anzeigen", "16");

        stack.Children.Add(new TextBlock
        {
            Text = "IPv6-Beispiel: 2001:db8:10::/48 mit neuem Präfix 64",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, -4, 0, 10)
        });

        var calculateButton = new Button
        {
            Content = "Subnetze berechnen",
            Style = TryFindResource("PrimaryButtonStyle") as Style,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 18),
            Padding = new Thickness(14, 8, 14, 8)
        };
        stack.Children.Add(calculateButton);

        stack.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            Margin = new Thickness(0, 4, 0, 16)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Einzel-IP prüfen",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Prüft IPv4 oder IPv6, berechnet das zugehörige Netz und zeigt Adressbereich, Präfixmaske sowie protokollspezifische Angaben an.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(174, 184, 197)),
            Margin = new Thickness(0, 0, 0, 12)
        });

        var ip = AddCheckField(stack, "IP-Adresse (IPv4 oder IPv6)", "192.168.10.25");
        var mask = AddCheckField(stack, "Präfix / Subnetzmaske", "/24");
        var wildcard = AddCheckField(stack, "Wildcard optional (nur IPv4)", "");

        stack.Children.Add(new TextBlock
        {
            Text = "IPv6-Beispiel: 2001:db8:10::25 mit /64. Alternativ kann die Adresse direkt als 2001:db8:10::25/64 eingegeben werden.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, -4, 0, 10)
        });

        var checkButton = new Button
        {
            Content = "IP prüfen",
            Style = TryFindResource("PrimaryButtonStyle") as Style,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 8),
            Padding = new Thickness(14, 8, 14, 8)
        };
        stack.Children.Add(checkButton);

        var inputScroll = new ScrollViewer
        {
            Content = inputCard,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly,
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetColumn(inputScroll, 0);
        grid.Children.Add(inputScroll);

        var resultCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 25)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18),
            BorderBrush = new SolidColorBrush(Color.FromRgb(154, 79, 24)),
            BorderThickness = new Thickness(1)
        };
        Grid.SetColumn(resultCard, 1);

        var resultGrid = new Grid();
        resultGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        resultGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        resultGrid.Children.Add(new TextBlock
        {
            Text = "Ergebnis",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var output = new TextBox
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(8, 11, 16)),
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Text = "Bereit.\n\nIPv4-Beispiel:\nNetz / CIDR: 192.168.10.0/24\nNeuer Präfix: 26\n\nIPv6-Beispiel:\nNetz / CIDR: 2001:db8:10::/48\nNeuer Präfix: 64\n\nBeide Ergebnisbereiche sind vertikal und horizontal scrollbar."
        };
        Grid.SetRow(output, 1);
        resultGrid.Children.Add(output);

        resultCard.Child = resultGrid;
        grid.Children.Add(resultCard);

        calculateButton.Click += (_, _) =>
        {
            SubnettingCalculator.TryCalculate(network.Text, newPrefix.Text, maxSubnets.Text, out var result);
            output.Text = result;
            output.ScrollToHome();
        };

        checkButton.Click += (_, _) =>
        {
            IpCalculator.TryCalculate(ip.Text, mask.Text, wildcard.Text, out var result);
            output.Text = result;
            output.ScrollToHome();
        };

        tab.Content = grid;
        _tabsByName["Subnetting"] = tab;
        MainTabs.Items.Add(tab);
    }

    private static TextBox AddCheckField(Panel parent, string label, string value)
    {
        parent.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4)
        });
        var tb = new TextBox
        {
            Text = value,
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        parent.Children.Add(tb);
        return tb;
    }

    private void SetTabChecks(string tabName, bool value)
    {
        if (!_modulesByTab.TryGetValue(tabName, out var modules)) return;
        InvalidateGeneratedState();
        foreach (var module in modules.Where(IsAllowed))
            _moduleChecks[module.Name].IsChecked = value;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        foreach (var module in ModuleCatalog.All)
        {
            var allowed = IsAllowed(module);
            var searchMatch = MatchesTabSearch(module);
            var enabled = allowed && (_moduleChecks.TryGetValue(module.Name, out var cb) && cb.IsChecked == true);
            var visible = enabled && searchMatch;

            if (_moduleRows.TryGetValue(module.Name, out var rowElement))
            {
                rowElement.Visibility = allowed && searchMatch ? Visibility.Visible : Visibility.Collapsed;
                rowElement.Opacity = allowed ? 1.0 : 0.42;
                if (rowElement is Border row)
                {
                    row.Background = enabled
                        ? new SolidColorBrush(Color.FromRgb(44, 26, 12))
                        : Brushes.Transparent;
                    row.BorderBrush = enabled
                        ? new SolidColorBrush(Color.FromRgb(154, 79, 24))
                        : new SolidColorBrush(Color.FromRgb(31, 37, 47));
                }
            }

            if (_moduleCards.TryGetValue(module.Name, out var card))
            {
                card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                card.Padding = new Thickness(0);
                card.Margin = new Thickness(0, 0, 0, 10);
            }

            UpdateModuleStatus(module, allowed, enabled);
        }

        foreach (var (tabName, modules) in _modulesByTab)
        {
            var available = modules.Count(IsAllowed);
            var active = modules.Count(m => IsAllowed(m) && _moduleChecks.TryGetValue(m.Name, out var cb) && cb.IsChecked == true);
            if (_tabActiveCountTexts.TryGetValue(tabName, out var activeText))
                activeText.Text = $"{active} aktiv";
            if (_tabModuleCountTexts.TryGetValue(tabName, out var listText))
                listText.Text = $"{active} / {available}";
            if (_tabEmptyStates.TryGetValue(tabName, out var emptyState))
                emptyState.Visibility = active == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        ApplyLayoutMode(false);
        UpdateStatusBar();
    }

    private void ApplyModulePreset(string preset)
    {
        InvalidateGeneratedState();
        foreach (var module in ModuleCatalog.All)
        {
            if (!_moduleChecks.TryGetValue(module.Name, out var cb)) continue;
            if (!IsAllowed(module))
            {
                cb.IsChecked = false;
                continue;
            }

            cb.IsChecked = preset switch
            {
                "Favoriten" => _favoriteModules.Contains(module.Name),
                "Basis" => module.Tab.Equals("Basis", StringComparison.OrdinalIgnoreCase) || module.Name.Contains("basic", StringComparison.OrdinalIgnoreCase) || module.Name.Contains("ssh", StringComparison.OrdinalIgnoreCase),
                "Management" => module.Tab.Equals("Management", StringComparison.OrdinalIgnoreCase) || module.Name.Contains("ntp", StringComparison.OrdinalIgnoreCase) || module.Name.Contains("syslog", StringComparison.OrdinalIgnoreCase) || module.Name.Contains("snmp", StringComparison.OrdinalIgnoreCase),
                "Switching" => module.Tab.Equals("Switching", StringComparison.OrdinalIgnoreCase) || module.Tab.Equals("Interfaces", StringComparison.OrdinalIgnoreCase),
                "Routing" => module.Tab.Equals("Routing", StringComparison.OrdinalIgnoreCase),
                "Security" => module.Tab.Equals("Security/WAN", StringComparison.OrdinalIgnoreCase) || module.Tab.Equals("IPv6/DHCP/ACL", StringComparison.OrdinalIgnoreCase),
                _ => cb.IsChecked
            };
        }

        ApplyFilters();
    }

    private bool MatchesTabSearch(ModuleDefinition module)
    {
        if (!_tabSearchText.TryGetValue(module.Tab, out var query) || string.IsNullOrWhiteSpace(query))
            return true;

        query = query.Trim();
        if (Contains(module.Title, query) || Contains(module.Name, query) || Contains(module.Tab, query))
            return true;

        return module.Fields.Any(f => Contains(f.Label, query) || Contains(f.Name, query) || Contains(f.Help, query));

        static bool Contains(string source, string q) =>
            !string.IsNullOrWhiteSpace(source) && source.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompactMode() => false;

    private void ApplyLayoutMode(bool forceExpansion = true)
    {
        if (!forceExpansion) return;

        foreach (var module in ModuleCatalog.All)
        {
            if (!_moduleExpanders.TryGetValue(module.Name, out var expander)) continue;
            expander.IsExpanded = _moduleChecks.TryGetValue(module.Name, out var cb) && cb.IsChecked == true;
        }
    }

    private void UpdateModuleStatuses()
    {
        foreach (var module in ModuleCatalog.All)
        {
            var allowed = IsAllowed(module);
            var enabled = allowed && (_moduleChecks.TryGetValue(module.Name, out var cb) && cb.IsChecked == true);
            UpdateModuleStatus(module, allowed, enabled);
        }
    }

    private void UpdateModuleStatus(ModuleDefinition module, bool allowed, bool enabled)
    {
        if (!_moduleStatusBadges.TryGetValue(module.Name, out var badge) || !_moduleStatusTexts.TryGetValue(module.Name, out var text))
            return;

        if (!allowed)
        {
            SetStatusBadge(badge, text, "Nicht passend", Color.FromRgb(71, 85, 105), "Dieses Modul passt nicht zum aktuellen Gerätetyp oder VRF-Modus.");
            return;
        }

        if (!enabled)
        {
            SetStatusBadge(badge, text, "Aus", Color.FromRgb(71, 85, 105), "Dieses Modul ist nicht aktiv und wird nicht exportiert.");
            return;
        }

        var warnings = ValidateModule(module).ToList();
        if (warnings.Count == 0)
            SetStatusBadge(badge, text, "OK", Color.FromRgb(22, 101, 52), "Modul aktiv. Keine offensichtlichen Pflichtfeld-Warnungen erkannt.");
        else
            SetStatusBadge(badge, text, $"Prüfen {warnings.Count}", Color.FromRgb(146, 64, 14), string.Join("\n", warnings));
    }

    private static void SetStatusBadge(Border badge, TextBlock text, string label, Color color, string tooltip)
    {
        badge.Background = new SolidColorBrush(color);
        badge.BorderBrush = new SolidColorBrush(Color.FromRgb(
            (byte)Math.Min(255, color.R + 45),
            (byte)Math.Min(255, color.G + 45),
            (byte)Math.Min(255, color.B + 45)));
        badge.ToolTip = tooltip;
        text.Text = label;
    }

    private IEnumerable<string> ValidateModule(ModuleDefinition module)
    {
        foreach (var field in module.Fields)
        {
            if (!IsFieldApplicable(field) || !LooksRequired(field)) continue;
            var value = GetFieldValue(field.Name);
            if (string.IsNullOrWhiteSpace(value))
                yield return $"{module.Title}: Feld '{field.Label}' ist leer.";
        }

        if (module.Name.Equals("stpExtended", StringComparison.OrdinalIgnoreCase))
        {
            var request = BuildRequest();
            foreach (var warning in StpValidationService.Validate(request.Values, request.Modules))
                yield return warning.Message;
        }
    }

    private IReadOnlyList<UiValidationIssue> GetCurrentValidationIssues()
    {
        var issues = new List<UiValidationIssue>();
        var activeModules = ModuleCatalog.All
            .Where(m => IsAllowed(m) && _moduleChecks.TryGetValue(m.Name, out var cb) && cb.IsChecked == true)
            .ToList();

        if (activeModules.Count == 0)
        {
            issues.Add(new UiValidationIssue(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                "Keine Module aktiv. Es wird praktisch keine Konfiguration erzeugt."));
            return issues;
        }

        foreach (var module in activeModules)
        {
            foreach (var field in module.Fields)
            {
                if (!IsFieldApplicable(field) || !LooksRequired(field)) continue;
                if (!string.IsNullOrWhiteSpace(GetFieldValue(field.Name))) continue;

                issues.Add(new UiValidationIssue(
                    module.Tab,
                    module.Name,
                    module.Title,
                    field.Name,
                    field.Label,
                    $"[{module.Tab} > {module.Title} > {field.Label}] Pflichtfeld ist leer."));
            }

            if (module.Name.Equals("stpExtended", StringComparison.OrdinalIgnoreCase))
            {
                var request = BuildRequest();
                foreach (var warning in StpValidationService.Validate(request.Values, request.Modules))
                {
                    var field = module.Fields.FirstOrDefault(x => x.Name.Equals(warning.FieldName, StringComparison.OrdinalIgnoreCase));
                    issues.Add(new UiValidationIssue(
                        module.Tab,
                        module.Name,
                        module.Title,
                        warning.FieldName,
                        field?.Label ?? warning.FieldName,
                        $"[{module.Tab} > {module.Title} > {field?.Label ?? warning.FieldName}] {warning.Message}"));
                }
            }
        }

        return issues;
    }

    private bool LooksRequired(FieldDefinition field)
    {
        var label = field.Label ?? string.Empty;
        var name = field.Name ?? string.Empty;
        var help = field.Help ?? string.Empty;

        if (label.Contains("optional", StringComparison.OrdinalIgnoreCase) || name.Contains("optional", StringComparison.OrdinalIgnoreCase)) return false;
        if (label.Contains("Beschreibung", StringComparison.OrdinalIgnoreCase) || label.Contains("Remark", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Contains("desc", StringComparison.OrdinalIgnoreCase) || name.Contains("remark", StringComparison.OrdinalIgnoreCase)) return false;
        if (help.Contains("optional", StringComparison.OrdinalIgnoreCase)) return false;
        if (field.Type.Equals("Combo", StringComparison.OrdinalIgnoreCase) || field.Type.Equals("Preview", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.StartsWith("stp", StringComparison.OrdinalIgnoreCase)) return false;

        var requiredHints = new[] { "hostname", "domain", "interface", "vlan", "ip", "mask", "gateway", "next", "area", "process", "as", "neighbor", "secret", "password", "vrf", "route" };
        return requiredHints.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase) || label.Contains(h, StringComparison.OrdinalIgnoreCase));
    }

    private string GetFieldValue(string fieldName)
    {
        if (!_valueControls.TryGetValue(fieldName, out var control)) return string.Empty;

        return control switch
        {
            TextBox tb => tb.Text ?? string.Empty,
            ComboBox cb => cb.SelectedItem?.ToString() ?? string.Empty,
            CheckBox chk => chk.IsChecked == true ? "Ja" : "Nein",
            _ => string.Empty
        };
    }

    private void UpdateStatusBar()
    {
        _currentValidationIssues = GetCurrentValidationIssues();

        var hasWarnings = _currentValidationIssues.Count > 0;
        var validationText = hasWarnings
            ? $"Warnung: {_currentValidationIssues[0].Message}" + (_currentValidationIssues.Count > 1 ? $" (+{_currentValidationIssues.Count - 1})" : string.Empty)
            : "Validierung: keine offensichtlichen Pflichtfeld-Warnungen";

        if (_duplicateCheckHasRun && _lastDuplicateConfigIssues.Count > 0)
            validationText += $" · Duplikate: {_lastDuplicateConfigIssues.Count}";

        ValidationTextBlock.Text = validationText;
        var statusOk = !hasWarnings && (!_duplicateCheckHasRun || _lastDuplicateConfigIssues.Count == 0);
        ValidationTextBlock.Foreground = statusOk
            ? new SolidColorBrush(Color.FromRgb(134, 239, 172))
            : new SolidColorBrush(Color.FromRgb(251, 191, 36));
        StatusDot.Foreground = ValidationTextBlock.Foreground;

        var active = ModuleCatalog.All.Count(m => IsAllowed(m) && _moduleChecks.TryGetValue(m.Name, out var cb) && cb.IsChecked == true);
        var available = ModuleCatalog.All.Count(IsAllowed);
        ModuleSummaryTextBlock.Text = $"{active} von {available} Modulen aktiv";
        if (_dashboardDeviceText != null)
            _dashboardDeviceText.Text = $"{DeviceTypeCombo.SelectedItem ?? "Router"} · {ConfigModeCombo.SelectedItem ?? "Ohne VRF"}";
        if (_dashboardActiveModulesText != null)
            _dashboardActiveModulesText.Text = $"{active} von {available}";
        if (_dashboardValidationText != null)
        {
            _dashboardValidationText.Text = statusOk ? "Keine Warnungen" : $"{_currentValidationIssues.Count} Hinweis(e)";
            _dashboardValidationText.Foreground = statusOk
                ? new SolidColorBrush(Color.FromRgb(74, 222, 128))
                : new SolidColorBrush(Color.FromRgb(251, 191, 36));
        }

        if (hasWarnings)
        {
            var details = string.Join(Environment.NewLine, _currentValidationIssues.Select(issue => $"• {issue.Message}"));
            ValidationTextBlock.ToolTip = TooltipBuilder.Create(
                "Validierungswarnungen",
                $"Klicke auf die Statuszeile, um zur ersten Warnung zu springen.\n\n{details}");
        }
        else if (_duplicateCheckHasRun && _lastDuplicateConfigIssues.Count > 0)
        {
            ValidationTextBlock.ToolTip = TooltipBuilder.Create(
                "Duplikate erkannt",
                "Öffne den Bereich Ausgabe und prüfe den dort angezeigten Duplikat-Hinweis.");
        }
        else
        {
            ValidationTextBlock.ToolTip = TooltipBuilder.Create(
                "Validierung",
                "Für die aktuell aktivierten Module wurden keine offensichtlichen leeren Pflichtfelder erkannt.");
        }
    }

    private void NavigateToFirstValidationIssue()
    {
        var issue = _currentValidationIssues.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.ModuleName));
        if (issue is null) return;

        if (_tabsByName.TryGetValue(issue.Tab, out var tab))
            MainTabs.SelectedItem = tab;

        if (_moduleChecks.TryGetValue(issue.ModuleName, out var moduleCheck))
            moduleCheck.IsChecked = true;

        if (_moduleExpanders.TryGetValue(issue.ModuleName, out var expander))
            expander.IsExpanded = true;

        ApplyFilters();

        if (_moduleCards.TryGetValue(issue.ModuleName, out var card))
            card.BringIntoView();

        if (_valueControls.TryGetValue(issue.FieldName, out var control))
        {
            control.BringIntoView();
            control.Focus();
        }
    }

    private static IEnumerable<ModuleDefinition> OrderModules(string tabName, IEnumerable<ModuleDefinition> modules)
    {
        if (!ModuleOrder.TryGetValue(tabName, out var order))
            return modules.OrderBy(m => m.Title, StringComparer.OrdinalIgnoreCase);

        var positions = order
            .Select((name, index) => new { name, index })
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        return modules.OrderBy(m => positions.TryGetValue(m.Name, out var index) ? index : int.MaxValue)
                      .ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase);
    }

    private static string DisplayNameForTab(string tab) => tab switch
    {
        "IPv6/DHCP/ACL" => "Netzdienste",
        "Security/WAN" => "Sicherheit / WAN",
        _ => tab
    };

    private static string DescriptionForTab(string tab) => tab switch
    {
        "Basis" => "Gerätegrunddaten, Zeiteinstellungen und Banner zentral festlegen.",
        "Management" => "Administrationszugänge, Benutzer, AAA und Monitoring konfigurieren.",
        "Interfaces" => "Physische Ports, Ranges, Trunks, EtherChannel und Subinterfaces verwalten.",
        "Switching" => "VLANs, Spanning Tree, Access-Sicherheit und Layer-2-Schutzmechanismen.",
        "Routing" => "IPv4-Routing, Routingprotokolle, Redundanz, VRF und MPLS.",
        "IPv6/DHCP/ACL" => "Adressvergabe, ACLs und IPv6-Funktionen übersichtlich zusammengeführt.",
        "Security/WAN" => "Hardening, NAT, Firewalls, WAN-Redundanz und VPN-Technologien.",
        _ => "Module dieses Konfigurationsbereichs auswählen und bearbeiten."
    };

    private static string IconForTab(string tab) => tab switch
    {
        "Basis" => "◆",
        "Management" => "⚙",
        "Interfaces" => "↔",
        "Switching" => "⇄",
        "Routing" => "◇",
        "Security/WAN" => "▣",
        "IPv6/DHCP/ACL" => "◎",
        _ => "▪"
    };

    private static Color GetModuleColor(string tab) => tab switch
    {
        "Basis" => Color.FromRgb(232, 121, 26),
        "Management" => Color.FromRgb(167, 139, 250),
        "Interfaces" => Color.FromRgb(251, 146, 60),
        "Switching" => Color.FromRgb(74, 222, 128),
        "Routing" => Color.FromRgb(45, 212, 191),
        "Security/WAN" => Color.FromRgb(248, 113, 113),
        "IPv6/DHCP/ACL" => Color.FromRgb(250, 204, 21),
        _ => Color.FromRgb(148, 163, 184)
    };

    private void SetConfigurationPreviewText(string text)
    {
        _configurationPreviewText = text ?? string.Empty;
        if (_configurationPreviewBox == null) return;

        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            PagePadding = new Thickness(0)
        };

        foreach (var rawLine in _configurationPreviewText.Replace("\r\n", "\n").Split('\n'))
        {
            var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 18 };
            foreach (var run in CreateHighlightedRuns(rawLine))
                paragraph.Inlines.Add(run);
            document.Blocks.Add(paragraph);
        }

        _configurationPreviewBox.Document = document;
    }

    private IEnumerable<Run> CreateHighlightedRuns(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("!", StringComparison.OrdinalIgnoreCase))
        {
            yield return new Run(line) { Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)) };
            yield break;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            yield return new Run(" ") { Foreground = new SolidColorBrush(Color.FromRgb(229, 231, 235)) };
            yield break;
        }

        var leading = line.Length - trimmed.Length;
        if (leading > 0)
            yield return new Run(line[..leading]) { Foreground = new SolidColorBrush(Color.FromRgb(229, 231, 235)) };

        var firstSpace = trimmed.IndexOf(' ');
        var first = firstSpace < 0 ? trimmed : trimmed[..firstSpace];
        var rest = firstSpace < 0 ? string.Empty : trimmed[firstSpace..];
        var keywordColor = first switch
        {
            "interface" => Color.FromRgb(56, 189, 248),
            "router" => Color.FromRgb(45, 212, 191),
            "ip" => Color.FromRgb(34, 197, 94),
            "ipv6" => Color.FromRgb(34, 197, 94),
            "crypto" => Color.FromRgb(248, 113, 113),
            "access-list" => Color.FromRgb(251, 191, 36),
            "username" => Color.FromRgb(196, 181, 253),
            "line" => Color.FromRgb(196, 181, 253),
            "switchport" => Color.FromRgb(251, 146, 60),
            _ => Color.FromRgb(147, 197, 253)
        };

        yield return new Run(first) { Foreground = new SolidColorBrush(keywordColor), FontWeight = FontWeights.SemiBold };
        if (!string.IsNullOrEmpty(rest))
            yield return new Run(rest) { Foreground = new SolidColorBrush(Color.FromRgb(229, 231, 235)) };
    }

    private bool IsAllowed(ModuleDefinition module)
    {
        var device = DeviceTypeCombo.SelectedItem?.ToString() ?? "Router";
        var mode = ConfigModeCombo.SelectedItem?.ToString() ?? "Ohne VRF";
        var deviceAllowed = module.Devices.Contains("All") || module.Devices.Contains(device);
        if (!deviceAllowed) return false;

        if (mode == "Mit VRF")
            return !NoVrfOnlyModules.Contains(module.Name);

        return !VrfOnlyModules.Contains(module.Name);
    }


    private GenerationRequest BuildRequest()
    {
        var values = CollectValues();

        var modules = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in ModuleCatalog.All)
        {
            modules[module.Name] =
                IsAllowed(module)
                && _moduleChecks.TryGetValue(module.Name, out var cb)
                && cb.IsChecked == true;
        }

        return new GenerationRequest
        {
            Values = values,
            Modules = modules
        };
    }

    private Dictionary<string, string> CollectValues()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["deviceType"] = DeviceTypeCombo.SelectedItem?.ToString() ?? "Router",
            ["configMode"] = ConfigModeCombo.SelectedItem?.ToString() ?? "Ohne VRF",
            ["writeMem"] = WriteMemCombo.SelectedItem?.ToString() ?? "Ja"
        };

        foreach (var item in _valueControls)
        {
            var name = item.Key;
            var control = item.Value;

            values[name] = control switch
            {
                TextBox tb => tb.Text ?? string.Empty,
                ComboBox cb => cb.SelectedItem?.ToString() ?? string.Empty,
                CheckBox chk => chk.IsChecked == true ? "Ja" : "Nein",
                _ => string.Empty
            };
        }

        return values;
    }

    private void InvalidateGeneratedState()
    {
        _lastDuplicateConfigIssues = Array.Empty<DuplicateConfigIssue>();
        _duplicateCheckHasRun = false;
        _peerRequirementsText = string.Empty;
        if (_peerRequirementsBox != null)
            _peerRequirementsBox.Text = "Eingaben wurden geändert. Klicke auf 'Gegenstelle aktualisieren'.";
    }

    private string AddDuplicateReportToPreview(string config)
    {
        if (!_duplicateCheckHasRun || _lastDuplicateConfigIssues.Count == 0)
            return config;

        var sb = new StringBuilder();
        sb.AppendLine("! DUPLIKATPRÜFUNG");
        sb.AppendLine($"! Ergebnis: {_lastDuplicateConfigIssues.Count} mögliche doppelte Konfigurationsbefehle erkannt.");
        sb.AppendLine("! Bewertung: Kontextgleich doppelte Zeilen prüfen und nach Bedarf bereinigen.");
        sb.AppendLine("!");

        foreach (var issue in _lastDuplicateConfigIssues.Take(25))
            sb.AppendLine($"! - [{issue.Context}] {issue.Command} | {issue.Count}x | Zeilen: {string.Join(", ", issue.Lines)}");

        if (_lastDuplicateConfigIssues.Count > 25)
            sb.AppendLine($"! - weitere {_lastDuplicateConfigIssues.Count - 25} Einträge ausgeblendet.");

        sb.AppendLine("!");
        sb.AppendLine(config);
        return sb.ToString();
    }

    private void ShowDuplicateWarningIfNeeded(string action)
    {
        if (!_duplicateCheckHasRun || _lastDuplicateConfigIssues.Count == 0) return;

        var preview = string.Join(Environment.NewLine, _lastDuplicateConfigIssues.Take(8).Select(i =>
            $"- [{i.Context}] {i.Command} ({i.Count}x, Zeilen: {string.Join(", ", i.Lines)})"));

        if (_lastDuplicateConfigIssues.Count > 8)
            preview += Environment.NewLine + $"- weitere {_lastDuplicateConfigIssues.Count - 8} Einträge";

        MessageBox.Show(this,
            $"Die Konfiguration wurde {action}, aber die Duplikatprüfung hat mögliche doppelte Befehle erkannt.\n\n{preview}",
            "Duplikatprüfung",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static IReadOnlyList<DuplicateConfigIssue> FindDuplicateConfigIssues(string? config)
    {
        if (string.IsNullOrWhiteSpace(config))
            return Array.Empty<DuplicateConfigIssue>();

        var occurrences = new Dictionary<string, (string Context, string Command, List<int> Lines)>(StringComparer.OrdinalIgnoreCase);
        var currentContext = "global";
        var lines = config.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (IsContextReset(trimmed))
            {
                currentContext = "global";
                continue;
            }

            if (ShouldIgnoreDuplicateLine(trimmed))
                continue;

            if (TryGetConfigContext(trimmed, currentContext, out var newContext))
            {
                currentContext = newContext;
                continue;
            }

            var normalizedCommand = NormalizeConfigCommand(trimmed);
            if (string.IsNullOrWhiteSpace(normalizedCommand))
                continue;

            var key = currentContext + "\u001F" + normalizedCommand;
            if (!occurrences.TryGetValue(key, out var entry))
            {
                entry = (currentContext, normalizedCommand, new List<int>());
                occurrences[key] = entry;
            }

            entry.Lines.Add(i + 1);
        }

        return occurrences.Values
            .Where(x => x.Lines.Count > 1)
            .OrderByDescending(x => x.Lines.Count)
            .ThenBy(x => x.Context, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Command, StringComparer.OrdinalIgnoreCase)
            .Select(x => new DuplicateConfigIssue(x.Context, x.Command, x.Lines.Count, x.Lines))
            .ToList();
    }

    private static bool ShouldIgnoreDuplicateLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return true;
        if (line.StartsWith("!", StringComparison.OrdinalIgnoreCase)) return true;

        var normalized = NormalizeConfigCommand(line);
        return normalized is
            "enable" or
            "configure terminal" or
            "conf t" or
            "end" or
            "exit" or
            "exit-address-family" or
            "write memory" or
            "wr" or
            "no shutdown";
    }

    private static bool IsContextReset(string line)
    {
        var normalized = NormalizeConfigCommand(line);
        return normalized is "exit" or "exit-address-family" or "end";
    }

    private static bool TryGetConfigContext(string line, string currentContext, out string context)
    {
        var normalized = NormalizeConfigCommand(line);
        var headers = new[]
        {
            "interface ", "router ", "line ", "vlan ", "ip dhcp pool ",
            "ip access-list ", "ipv6 access-list ", "route-map ", "class-map ",
            "policy-map ", "crypto isakmp policy ", "crypto ipsec transform-set ",
            "crypto map ", "key chain ", "vrf definition ", "ip vrf ",
            "control-plane", "voice class ", "dial-peer voice ", "telephony-service",
            "call-manager-fallback", "zone security ", "zone-pair security ",
            "parameter-map ", "object-group ", "ip sla ", "track "
        };

        if (headers.Any(h => normalized.StartsWith(h, StringComparison.OrdinalIgnoreCase) || normalized.Equals(h.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            context = normalized;
            return true;
        }

        if (normalized.StartsWith("address-family ", StringComparison.OrdinalIgnoreCase))
        {
            context = currentContext + " > " + normalized;
            return true;
        }

        context = currentContext;
        return false;
    }

    private static string NormalizeConfigCommand(string line) =>
        string.Join(" ", (line ?? string.Empty).Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));

    private async Task<string> GenerateConfigAsync()
    {
        var request = BuildRequest();
        var config = await NativeCiscoGenerator.GenerateAsync(request);
        _lastDuplicateConfigIssues = FindDuplicateConfigIssues(config);
        _duplicateCheckHasRun = true;
        UpdateStatusBar();
        return config;
    }

    private async Task CopyGeneratedConfigAsync()
    {
        try
        {
            var config = await GenerateConfigAsync();
            Clipboard.SetText(config ?? string.Empty);
            ShowDuplicateWarningIfNeeded("kopiert");
            MessageBox.Show(this, "Konfiguration wurde in die Zwischenablage kopiert.", "Kopiert", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Fehler beim Erzeugen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SaveTextExportAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Cisco-Konfiguration speichern",
            Filter = "Textdatei (*.txt)|*.txt|Alle Dateien (*.*)|*.*",
            FileName = "cisco_config.txt"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var config = await GenerateConfigAsync();
            File.WriteAllText(dialog.FileName, config ?? string.Empty, new UTF8Encoding(false));
            ShowDuplicateWarningIfNeeded("exportiert");
            MessageBox.Show(this, "Konfiguration wurde gespeichert.", "Export abgeschlossen", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Fehler beim Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    private void SelectComPort()
    {
        var physicalPorts = SerialPortService.GetPorts();
        var ports = physicalPorts
            .Concat(new[] { SerialPortService.TestPortName })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var dialog = new Window
        {
            Title = "COM-Port / Testmodus auswählen",
            Owner = this,
            Width = 470,
            Height = 355,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 25))
        };

        var root = new StackPanel { Margin = new Thickness(18) };
        dialog.Content = root;

        root.Children.Add(new TextBlock
        {
            Text = "Serielle Verbindung",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        root.Children.Add(new TextBlock
        {
            Text = physicalPorts.Length == 0
                ? "Kein echter COM-Port gefunden. Nutze TEST-COM, um das Senden ohne Hardware in eine Logdatei zu prüfen."
                : "Wähle einen echten COM-Port oder TEST-COM für einen reinen Logdatei-Test.",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var selectedPort = string.IsNullOrWhiteSpace(_serialSettings.PortName)
            ? (physicalPorts.Length == 0 ? SerialPortService.TestPortName : physicalPorts[0])
            : _serialSettings.PortName;

        var portBox = AddSerialCombo(root, "COM-Port", ports, selectedPort);
        var baudBox = AddSerialCombo(root, "Baudrate", new[] { "9600", "19200", "38400", "57600", "115200" }, _serialSettings.BaudRate.ToString());
        var delayBox = AddSerialCombo(root, "Zeilenverzögerung", new[] { "10", "20", "35", "50", "100", "200" }, _serialSettings.LineDelayMs.ToString());

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "Übernehmen", MinWidth = 105 };
        var test = new Button { Content = "Testmodus", MinWidth = 105 };
        var cancel = new Button { Content = "Abbrechen", MinWidth = 95 };
        buttons.Children.Add(test);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        void ApplySelected(string? forcedPort = null)
        {
            if (!int.TryParse(baudBox.SelectedItem?.ToString(), out var baud)) baud = 9600;
            if (!int.TryParse(delayBox.SelectedItem?.ToString(), out var delay)) delay = 35;

            var port = forcedPort ?? portBox.SelectedItem?.ToString() ?? SerialPortService.TestPortName;
            _serialTestMode = string.Equals(port, SerialPortService.TestPortName, StringComparison.OrdinalIgnoreCase);
            _serialSettings = new SerialPortSettings(port, baud, 8, Parity.None, StopBits.One, delay);

            ValidationTextBlock.Text = _serialTestMode
                ? $"COM-Testmodus aktiv · {_serialSettings.BaudRate} Baud · Delay {_serialSettings.LineDelayMs} ms · Logdatei statt echter COM-Port"
                : $"COM-Port gewählt: {_serialSettings.PortName} · {_serialSettings.BaudRate} Baud · Delay {_serialSettings.LineDelayMs} ms";
            ValidationTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(134, 239, 172));
        }

        ok.Click += (_, _) =>
        {
            ApplySelected();
            dialog.DialogResult = true;
            dialog.Close();
        };
        test.Click += (_, _) =>
        {
            ApplySelected(SerialPortService.TestPortName);
            dialog.DialogResult = true;
            dialog.Close();
        };
        cancel.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };

        dialog.ShowDialog();
    }

    private void EnableComTestMode()
    {
        _serialTestMode = true;
        _serialSettings = _serialSettings with { PortName = SerialPortService.TestPortName };
        ValidationTextBlock.Text = $"COM-Testmodus aktiv · {_serialSettings.BaudRate} Baud · Delay {_serialSettings.LineDelayMs} ms · Logdatei statt echter COM-Port";
        ValidationTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(134, 239, 172));
    }

    private static ComboBox AddSerialCombo(Panel parent, string label, IEnumerable<string> values, string selected)
    {
        parent.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 4) });
        var combo = new ComboBox
        {
            ItemsSource = values.ToArray(),
            MinHeight = 32,
            Margin = new Thickness(0, 0, 0, 8)
        };
        combo.SelectedItem = combo.Items.Cast<object>().FirstOrDefault(x => string.Equals(x.ToString(), selected, StringComparison.OrdinalIgnoreCase)) ?? combo.Items[0];
        parent.Children.Add(combo);
        return combo;
    }

    private async Task SendConfigToComPortAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_serialSettings.PortName))
                SelectComPort();

            if (string.IsNullOrWhiteSpace(_serialSettings.PortName))
                return;

            var isTestMode = _serialTestMode || string.Equals(_serialSettings.PortName, SerialPortService.TestPortName, StringComparison.OrdinalIgnoreCase);
            var message = isTestMode
                ? "Die aktuelle Konfiguration wird im COM-Testmodus verarbeitet.\n\nEs wird kein echter COM-Port verwendet.\nDie Ausgabe wird zeilenweise in eine Logdatei geschrieben.\n\nFortfahren?"
                : $"Die aktuelle Konfiguration wird über {_serialSettings.PortName} mit {_serialSettings.BaudRate} Baud gesendet.\n\nVorher sicherstellen:\n- Cisco-Konsole ist verbunden\n- CLI ist bereit\n- Gerät soll diese Konfiguration erhalten\n\nFortfahren?";

            var result = MessageBox.Show(this,
                message,
                isTestMode ? "COM-Testmodus" : "An COM senden",
                MessageBoxButton.YesNo,
                isTestMode ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var config = await GenerateConfigAsync();

            if (isTestMode)
            {
                var logPath = await SerialPortService.SendCiscoConfigTestAsync(config, _serialSettings);
                ShowDuplicateWarningIfNeeded("im COM-Testmodus geschrieben");
                MessageBox.Show(this,
                    $"COM-Test abgeschlossen.\n\nLogdatei:\n{logPath}",
                    "COM-Testmodus abgeschlossen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            await SerialPortService.SendCiscoConfigAsync(config, _serialSettings);
            ShowDuplicateWarningIfNeeded("über COM gesendet");
            MessageBox.Show(this, $"Konfiguration wurde über {_serialSettings.PortName} gesendet.", "COM abgeschlossen", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "COM-Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetAllInputs()
    {
        var result = MessageBox.Show(
            this,
            "Sollen wirklich alle Eingaben, aktivierten Module, Importdaten und Vorschauen auf die Ausgangswerte zurückgesetzt werden?",
            "Alles zurücksetzen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        DeviceTypeCombo.SelectedItem = "Router";
        ConfigModeCombo.SelectedItem = "Ohne VRF";
        WriteMemCombo.SelectedItem = "Ja";

        foreach (var module in ModuleCatalog.All)
        {
            if (_moduleChecks.TryGetValue(module.Name, out var moduleCheck))
                moduleCheck.IsChecked = module.Default;

            if (_moduleExpanders.TryGetValue(module.Name, out var expander))
                expander.IsExpanded = module.Default;

            foreach (var field in module.Fields)
            {
                if (!_valueControls.TryGetValue(field.Name, out var control)) continue;
                if (control is TextBox textBox)
                    textBox.Text = field.Value ?? string.Empty;
                else if (control is ComboBox comboBox)
                    comboBox.SelectedIndex = field.Items.Count == 0
                        ? -1
                        : Math.Clamp(field.Selected, 0, field.Items.Count - 1);
                else if (control is CheckBox checkBox)
                    checkBox.IsChecked = field.Value.Equals("Ja", StringComparison.OrdinalIgnoreCase);
            }
        }

        _lastImportAnalysis = null;
        if (_importConfigBox != null) _importConfigBox.Clear();
        if (_importResultBox != null) _importResultBox.Clear();
        _lastDuplicateConfigIssues = Array.Empty<DuplicateConfigIssue>();
        _duplicateCheckHasRun = false;
        _currentValidationIssues = Array.Empty<UiValidationIssue>();
        _configurationPreviewText = string.Empty;
        SetConfigurationPreviewText("Klicke auf 'Vorschau aktualisieren', um die aktuelle Cisco-Konfiguration zu erzeugen.");
        _peerRequirementsText = string.Empty;
        if (_peerRequirementsBox != null)
            _peerRequirementsBox.Text = "Klicke auf 'Gegenstelle aktualisieren', um die Anforderungen des Nachbargeräts zu erzeugen.";

        ApplyFilters();
        UpdateConditionalFieldVisibility();
        UpdateModuleStatuses();
        UpdateStatusBar();
        RefreshStpPreview();
        NavigateToTab("Übersicht");
    }

    private void SaveTemplate()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Vorlage speichern",
            Filter = "JSON Vorlage (*.json)|*.json|Alle Dateien (*.*)|*.*",
            FileName = "cisco_template.json"
        };
        if (dialog.ShowDialog(this) != true) return;

        var data = new TemplateData
        {
            Values = CollectValues(),
            Modules = ModuleCatalog.All.ToDictionary(m => m.Name, m => _moduleChecks.TryGetValue(m.Name, out var cb) && cb.IsChecked == true, StringComparer.OrdinalIgnoreCase)
        };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    private void LoadTemplate()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Vorlage laden",
            Filter = "JSON Vorlage (*.json)|*.json|Alle Dateien (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        var data = JsonSerializer.Deserialize<TemplateData>(File.ReadAllText(dialog.FileName, Encoding.UTF8), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (data == null) return;

        foreach (var (name, value) in data.Values)
        {
            if (name.Equals("deviceType", StringComparison.OrdinalIgnoreCase)) DeviceTypeCombo.SelectedItem = value;
            else if (name.Equals("configMode", StringComparison.OrdinalIgnoreCase)) ConfigModeCombo.SelectedItem = value;
            else if (name.Equals("writeMem", StringComparison.OrdinalIgnoreCase)) WriteMemCombo.SelectedItem = value;
            else if (_valueControls.TryGetValue(name, out var control))
            {
                if (control is TextBox tb) tb.Text = value;
                else if (control is ComboBox cb) cb.SelectedItem = value;
            }
        }

        foreach (var (name, active) in data.Modules)
            if (_moduleChecks.TryGetValue(name, out var cb)) cb.IsChecked = active;

        ApplyFilters();
        UpdateConditionalFieldVisibility();
        RefreshStpPreview();
    }
}
