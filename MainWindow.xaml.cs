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
    private readonly ConfigurationWorkflowService _configurationWorkflowService = new();
    private readonly ImportWorkflowService _importWorkflowService = new();
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
    private ConfigurationWorkflowResult? _lastConfigurationWorkflowResult;
    private SerialPortSettings _serialSettings = new("", 9600, 8, Parity.None, StopBits.One, 35);
    private bool _serialTestMode;
    private TextBox? _importConfigBox;
    private TextBox? _importResultBox;
    private ImportResult? _lastImportResult;
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
        // Grundaufbau: zuerst Identität und globale Geräteeinstellungen.
        ["Basis"] = new[] { "basic", "banner" },

        // login local benötigt zuerst einen lokalen Benutzer; danach folgen SSH, AAA und zentrale Dienste.
        ["Management"] = new[] { "userRights", "ssh", "aaa", "radiusTacacs", "monitoringBase" },

        // Von der Planung über einzelne Ports bis zu logischen und erweiterten Interfaces.
        ["Interfaces"] = new[] { "interfaceRoles", "interfaceProfiles", "interfaces", "ranges", "etherChannelExt", "trunkUplink", "subinterfaces", "routerStick", "qinq", "qosBasic" },

        // VLAN-Grundlage vor Access-Funktionen, STP und Schutzmechanismen.
        ["Switching"] = new[] { "vlanIpPlan", "vlans", "accessBaseline", "voip", "stpExtended", "portSecurityExt", "switchSec", "switchSecExt", "errdisableRecovery" },

        // Routing-Basis vor Protokollen, Richtlinien, Redundanz, VRF und MPLS.
        ["Routing"] = new[] { "routingBase", "staticRoutes", "ospf", "ospfAdvanced", "isis", "bgp", "bgpAdvanced", "routeMapFilter", "fhrp", "ipSlaTracking", "vrfLite", "vrfDefs", "vrfSvi", "vrfStaticRoutes", "vrfOspf", "vrfOspfv3", "vrfBgp", "mpls" },

        // Adressvergabe und Filter vor den erweiterten IPv6-Routingfunktionen.
        ["IPv6/DHCP/ACL"] = new[] { "dhcp", "aclAssistant", "acl", "ipv6", "ipv6Extended", "ospfv3", "ipv6RoutingProtocols" },

        // Hardening und Zonierung vor NAT, WAN-Redundanz und VPN.
        ["Security/WAN"] = new[] { "security", "zoneFirewall", "dmzAssistant", "nat", "wanFailover", "vpn", "greIpsec", "vpnAdvanced", "customCommands" }
    };

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
        InitializeApplicationSettings();

        var availableModules = ModuleCatalog.All;
        if (availableModules.Count == 0)
        {
            throw new InvalidDataException(
                "The embedded module catalog could not be loaded. " +
                "Check the localization resources and startup log.");
        }
        StartupDiagnostics.WriteInfo($"MainWindow uses {availableModules.Count} configuration modules.");

        InitTopBar();
        BuildDashboardTab();
        BuildModuleTabs();
        BuildConfigurationTab();
        BuildPeerRequirementsTab();
        BuildImportConfigTab();
        BuildCommandTab();
        BuildCheckTab();
        BuildAdvancedFeatureTabs();
        BuildSettingsTab();
        BuildPluginManagerTab();
        RebuildMainNavigation();
        var startPage = string.IsNullOrWhiteSpace(_appSettings.StartPage) ? "Übersicht" : _appSettings.StartPage;
        if (_tabsByName.TryGetValue(startPage, out var startTab))
            MainTabs.SelectedItem = startTab;
        else if (_tabsByName.TryGetValue("Übersicht", out var overviewTab))
            MainTabs.SelectedItem = overviewTab;
        HookButtons();
        ApplyFilters();
        UpdateConditionalFieldVisibility();
        RefreshStpPreview();
        ApplyRuntimeSettings();
    }

    private void InitTopBar()
    {
        DeviceTypeCombo.ItemsSource = new[] { "Router", "L3-Switch", "L2-Switch" };
        DeviceTypeCombo.ItemTemplate = LocalizationService.CreateLocalizedStringTemplate();
        DeviceTypeCombo.SelectedItem = DeviceTypeCombo.Items.Cast<object>().FirstOrDefault(x => string.Equals(x?.ToString(), _appSettings.DefaultDeviceType, StringComparison.OrdinalIgnoreCase)) ?? "Router";
        ConfigModeCombo.ItemsSource = new[] { "Ohne VRF", "Mit VRF" };
        ConfigModeCombo.ItemTemplate = LocalizationService.CreateLocalizedStringTemplate();
        ConfigModeCombo.SelectedItem = ConfigModeCombo.Items.Cast<object>().FirstOrDefault(x => string.Equals(x?.ToString(), _appSettings.DefaultConfigMode, StringComparison.OrdinalIgnoreCase)) ?? "Ohne VRF";
        WriteMemCombo.ItemsSource = new[] { "Ja", "Nein" };
        WriteMemCombo.ItemTemplate = LocalizationService.CreateLocalizedStringTemplate();
        WriteMemCombo.SelectedItem = _appSettings.IncludeWriteMemory ? "Ja" : "Nein";

        DeviceTypeCombo.SelectionChanged += (_, _) => { InvalidateGeneratedState(); ApplyFilters(); UpdateConditionalFieldVisibility(); RefreshStpPreview(); ScheduleAutoSave(); };
        ConfigModeCombo.SelectionChanged += (_, _) => { InvalidateGeneratedState(); ApplyFilters(); UpdateConditionalFieldVisibility(); RefreshStpPreview(); ScheduleAutoSave(); };
        WriteMemCombo.SelectionChanged += (_, _) => ScheduleAutoSave();

        DeviceTypeCombo.ToolTip = TooltipBuilder.Create(LocalizationService.Get("text.geratetyp"), LocalizationService.Get("text.zweck_filtert_module_passend_zum_zielgerat_router_routing_wa"));
        ConfigModeCombo.ToolTip = TooltipBuilder.Create("Konfigurationsmodus", "Ohne VRF:\nNormale globale IPv4/IPv6-Konfiguration.\n\nMit VRF:\nVRF-Definitionen, VRF-SVIs, VRF-Routen und VRF-Routingprotokolle werden eingeblendet; globale Routingmodule werden ausgeblendet.");
        WriteMemCombo.ToolTip = TooltipBuilder.Create("write memory", "Zweck:\nFügt am Ende write memory hinzu.\n\nHinweis:\nFür Labor meist praktisch. Produktiv nur setzen, wenn die Konfiguration vorher geprüft wurde.");
        ValidationTextBlock.MouseLeftButtonUp += (_, _) => NavigateToFirstValidationIssue();
    }

    private void HookButtons()
    {
        SaveTemplateButton.Click += (_, _) => SaveTemplate();
        LoadTemplateButton.Click += (_, _) => LoadTemplate();
        ResetAllButton.Click += (_, _) => ResetAllInputs();
    }

    private void BuildDashboardTab()
    {
        var tab = new TabItem { Header = "⌂  " + LocalizationService.Get("navigation.overview", "Übersicht") };
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
        heroText.Children.Add(new TextBlock { Text = LocalizationService.Get("text.konfigurationsubersicht"), FontSize = 26, FontWeight = FontWeights.Bold });
        heroText.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("text.gerat_auswahlen_benotigte_module_aktivieren_eingaben_prufen_"),
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
        _dashboardDeviceText = new TextBlock { Text = LocalizationService.Get("text.router"), FontSize = 19, FontWeight = FontWeights.Bold };
        _dashboardActiveModulesText = new TextBlock { Text = LocalizationService.Get("status.zero_modules"), FontSize = 19, FontWeight = FontWeights.Bold };
        _dashboardValidationText = new TextBlock { Text = LocalizationService.Get("text.bereit"), FontSize = 19, FontWeight = FontWeights.Bold };
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
        workflowStack.Children.Add(new TextBlock { Text = LocalizationService.Get("text.empfohlener_ablauf"), FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });
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
        quickStack.Children.Add(new TextBlock { Text = LocalizationService.Get("text.direktzugriff"), FontSize = 18, FontWeight = FontWeights.Bold });
        quickStack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("text.haufig_verwendete_bereiche_ohne_umweg_offnen"),
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
        quick.Children.Add(CreateQuickNavigationButton("Projekt", () => NavigateToTab("Projekt")));
        quick.Children.Add(CreateQuickNavigationButton("IPAM", () => NavigateToTab("IPAM / Ports")));
        quick.Children.Add(CreateQuickNavigationButton("Analyse", () => NavigateToTab("Analyse")));
        quick.Children.Add(CreateQuickNavigationButton("SSH", () => NavigateToTab("Betrieb")));
        quick.Children.Add(CreateQuickNavigationButton("Diagramm", () => NavigateToTab("Diagramm / Bericht")));
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

    private void RebuildMainNavigation()
    {
        // Die Hauptnavigation wird unabhängig von der Erzeugungsreihenfolge aufgebaut.
        // Dadurch bleiben Fachbereiche und Dokumentationswerkzeuge dauerhaft übersichtlich gruppiert.
        MainTabs.Items.Clear();

        AddNavigationGroupHeader(LocalizationService.Get("navigation.group.configuration", "KONFIGURATION"));
        foreach (var tabName in new[]
                 {
                     "Übersicht",
                     "Basis",
                     "Management",
                     "Interfaces",
                     "Switching",
                     "Routing",
                     "IPv6/DHCP/ACL",
                     "Security/WAN"
                 })
        {
            AddNavigationTab(tabName);
        }

        AddNavigationGroupHeader(LocalizationService.Get("navigation.group.tools", "WERKZEUGE"));
        foreach (var tabName in new[]
                 {
                     "Subnetting",
                     "Befehlsregister",
                     "Betrieb",
                     "Import",
                     "Gegenstelle",
                     "Ausgabe"
                 })
        {
            AddNavigationTab(tabName);
        }

        AddNavigationGroupHeader(LocalizationService.Get("navigation.group.documentation", "DOKUMENTATION"));
        foreach (var tabName in new[]
                 {
                     "Projekt",
                     "IPAM / Ports",
                     "Analyse",
                     "Diagramm / Bericht"
                 })
        {
            AddNavigationTab(tabName);
        }

        AddNavigationGroupHeader(LocalizationService.Get("navigation.group.system", "SYSTEM"));
        AddNavigationTab("Plugin-Manager");
        AddNavigationTab("Einstellungen");
    }

    private void AddNavigationTab(string tabName)
    {
        if (!_tabsByName.TryGetValue(tabName, out var tab)) return;

        tab.Header = $"{IconForTab(tabName)}  {DisplayNameForTab(tabName)}";
        MainTabs.Items.Add(tab);
    }

    private void AddNavigationGroupHeader(string title)
    {
        var header = new TabItem
        {
            Header = title,
            IsEnabled = false,
            Focusable = false,
            IsHitTestVisible = false,
            Style = TryFindResource("NavigationGroupHeaderStyle") as Style
        };
        MainTabs.Items.Add(header);
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
            Text = LocalizationService.Get("text.noch_kein_modul_aktiviert"),
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 4)
        });
        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("text.aktiviere_links_ein_modul_die_zugehorigen_eingaben_erscheine"),
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
            Text = LocalizationService.Get("status.zero_active"),
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

        var open = new Button { Content = LocalizationService.Get("text.alle_offnen"), Style = TryFindResource("SmallButtonStyle") as Style };
        var close = new Button { Content = LocalizationService.Get("text.alle_schlie_en"), Style = TryFindResource("SmallButtonStyle") as Style };
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
                new TextBlock { Text = LocalizationService.Get("common.modules"), FontSize = 17, FontWeight = FontWeights.Bold },
                new TextBlock { Text = LocalizationService.Get("text.auswahlen_und_konfigurieren"), FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(139, 150, 166)), Margin = new Thickness(0, 2, 0, 0) }
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
            Text = LocalizationService.Get("text.module_durchsuchen"),
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
            ToolTip = TooltipBuilder.Create(LocalizationService.Get("text.modulsuche"), LocalizationService.Get("text.durchsucht_die_module_dieses_bereichs_gesucht_wird_in_moduln"))
        };
        searchBox.TextChanged += (_, _) => { _tabSearchText[tabName] = searchBox.Text ?? string.Empty; ApplyFilters(); };
        DockPanel.SetDock(searchBox, Dock.Top);
        dock.Children.Add(searchBox);

        var buttons = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 10) };
        var all = new Button { Content = LocalizationService.Get("text.alle"), Style = TryFindResource("SmallButtonStyle") as Style, ToolTip = LocalizationService.Get("tooltip.enable_visible_modules") };
        var none = new Button { Content = LocalizationService.Get("text.keine"), Style = TryFindResource("SmallButtonStyle") as Style, ToolTip = LocalizationService.Get("tooltip.disable_section_modules") };
        var favOnly = new Button { Content = LocalizationService.Get("text.favoriten"), Style = TryFindResource("SmallButtonStyle") as Style, ToolTip = LocalizationService.Get("text.aktiviert_nur_die_markierten_favoritenmodule") };
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
                RefreshModuleLivePreview(module.Name);
                ScheduleAutoSave();
            };
            check.Unchecked += (_, _) =>
            {
                InvalidateGeneratedState();
                ApplyFilters();
                UpdateConditionalFieldVisibility();
                if (module.Name.Equals("stpExtended", StringComparison.OrdinalIgnoreCase)) RefreshStpPreview();
                RefreshModuleLivePreview(module.Name);
                ScheduleAutoSave();
            };

            var fav = new Button
            {
                Content = "☆",
                Width = 28,
                MinHeight = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(3, 0, 0, 0),
                Style = TryFindResource("SmallButtonStyle") as Style,
                ToolTip = LocalizationService.Get("text.als_favorit_markieren_oder_entfernen")
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
        panel.Children.Add(CreateModuleLivePreview(module));
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
            Text = LocalizationService.Get("text.aus"),
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
            ToolTip = LocalizationService.Get("text.validierungsstatus_dieses_moduls"),
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
            combo.ItemTemplate = LocalizationService.CreateLocalizedStringTemplate();
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
                text.Text = LocalizationService.Get("text.stp_modul_aktivieren_um_die_live_vorschau_anzuzeigen");
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

        RefreshModuleLivePreviewForField(fieldName);
        ScheduleAutoSave();
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
                preview.Text = LocalizationService.Get("text.stp_modul_aktivieren_um_die_live_vorschau_anzuzeigen");
                return;
            }

            var config = await _configurationWorkflowService.GenerateRawAsync(GetCurrentGenerationRequest());
            preview.Text = ExtractGeneratedSection(config, "STP ERWEITERT");
            preview.ScrollToHome();
        }
        catch (Exception ex)
        {
            preview.Text = LocalizationService.Get("text.vorschau_konnte_nicht_erzeugt_werden.0009bf22") + ex.Message;
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
        var tab = new TabItem { Header = LocalizationService.Get("tab.output") };
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
            Text = LocalizationService.Get("text.konfiguration_erzeugen"),
            FontSize = 19,
            FontWeight = FontWeights.Bold
        });
        info.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("text.aktive_module_zusammenfuhren_prufen_kopieren_oder_direkt_ube"),
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
            Content = LocalizationService.Get("text.vorschau_aktualisieren"),
            Style = TryFindResource("PrimaryButtonStyle") as Style,
            ToolTip = TooltipBuilder.Create(LocalizationService.Get("text.vorschau_aktualisieren"), LocalizationService.Get("text.erzeugt_die_cisco_konfiguration_anhand_der_aktuell_gesetzten"))
        };
        refreshButton.Click += async (_, _) => await RefreshConfigurationPreviewAsync();

        var copyButton = new Button
        {
            Content = LocalizationService.Get("text.vorschau_kopieren"),
            ToolTip = TooltipBuilder.Create(LocalizationService.Get("text.vorschau_kopieren"), LocalizationService.Get("text.kopiert_den_inhalt_des_vorschau_feldes_in_die_zwischenablage"))
        };
        copyButton.Click += async (_, _) => await CopyPreviewAsync();

        var exportButton = new Button
        {
            Content = LocalizationService.Get("header.txt_export"),
            ToolTip = TooltipBuilder.Create(LocalizationService.Get("header.txt_export"), LocalizationService.Get("text.erzeugt_die_konfiguration_im_hintergrund_und_speichert_sie_a"))
        };
        exportButton.Click += async (_, _) => await SaveTextExportAsync();

        var peerButton = new Button
        {
            Content = LocalizationService.Get("text.gegenstelle_anzeigen"),
            ToolTip = TooltipBuilder.Create(LocalizationService.Get("text.gegenstelle_anzeigen"), LocalizationService.Get("text.offnet_die_automatisch_abgeleiteten_anforderungen_und_beispi"))
        };
        peerButton.Click += (_, _) =>
        {
            NavigateToTab("Gegenstelle");
            RefreshPeerRequirements();
        };

        var comMenuButton = new Button
        {
            Content = LocalizationService.Get("text.com_konsole"),
            ToolTip = TooltipBuilder.Create(LocalizationService.Get("text.com_konsole"), LocalizationService.Get("text.fasst_die_serielle_ausgabe_zusammen_optionen_com_port_wahlen"))
        };

        var comMenu = new ContextMenu();
        var comSelectItem = new MenuItem { Header = LocalizationService.Get("text.com_port_wahlen") };
        comSelectItem.Click += (_, _) => SelectComPort();

        var comTestItem = new MenuItem { Header = LocalizationService.Get("text.com_testmodus_aktivieren") };
        comTestItem.Click += (_, _) => EnableComTestMode();

        var comSendItem = new MenuItem { Header = LocalizationService.Get("text.konfiguration_senden") };
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
        SetConfigurationPreviewText(LocalizationService.Get("configuration.workflow.preview.initial"));

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

            if (_configurationPreviewBox != null && _lastConfigurationWorkflowResult == null)
                await RefreshConfigurationPreviewAsync();
        };
    }

    private async Task RefreshConfigurationPreviewAsync()
    {
        if (_configurationPreviewBox == null) return;

        try
        {
            SetConfigurationPreviewText(LocalizationService.Get("configuration.workflow.preview.generating"));
            var result = await GenerateConfigurationWorkflowAsync();
            SetConfigurationPreviewText(result.Preview);
        }
        catch (Exception ex)
        {
            SetConfigurationPreviewText(LocalizationService.Format("configuration.workflow.preview.error", ex.Message));
        }
    }

    private async Task CopyPreviewAsync()
    {
        if (_configurationPreviewBox == null) return;

        try
        {
            var result = _lastConfigurationWorkflowResult ?? await GenerateConfigurationWorkflowAsync();
            var copyText = _configurationWorkflowService.GetCopyText(result, includePreview: true);
            SetConfigurationPreviewText(result.Preview);
            Clipboard.SetText(copyText);
            MessageBox.Show(this, LocalizationService.Get("text.vorschau_wurde_in_die_zwischenablage_kopiert"), LocalizationService.Get("text.kopiert"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.Get("text.fehler_beim_erzeugen"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }



    private void BuildPeerRequirementsTab()
    {
        var tab = new TabItem { Header = LocalizationService.Get("tab.peer") };
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
            Text = LocalizationService.Get("text.anforderungen_der_gegenstelle"),
            FontSize = 19,
            FontWeight = FontWeights.Bold
        });
        info.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("text.zeigt_zu_aktiven_trunks_etherchannels_und_routing_vpn_protok"),
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
            Content = LocalizationService.Get("peer.refresh"),
            Style = TryFindResource("PrimaryButtonStyle") as Style,
            ToolTip = LocalizationService.Get("text.leitet_die_anforderungen_erneut_aus_allen_aktuellen_eingaben")
        };
        refreshButton.Click += (_, _) => RefreshPeerRequirements();

        var copyButton = new Button
        {
            Content = LocalizationService.Get("common.copy"),
            ToolTip = LocalizationService.Get("text.kopiert_die_gegenstellenanforderungen_in_die_zwischenablage")
        };
        copyButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_peerRequirementsText)) RefreshPeerRequirements();
            Clipboard.SetText(_peerRequirementsText);
            MessageBox.Show(this, LocalizationService.Get("text.gegenstellenanforderungen_wurden_kopiert"), LocalizationService.Get("text.kopiert"), MessageBoxButton.OK, MessageBoxImage.Information);
        };

        var saveButton = new Button
        {
            Content = LocalizationService.Get("header.txt_export"),
            ToolTip = LocalizationService.Get("text.speichert_die_gegenstellenanforderungen_als_textdatei")
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
            Text = LocalizationService.Get("text.klicke_auf_gegenstelle_aktualisieren_um_die_anforderungen_de"),
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
            _peerRequirementsText = _configurationWorkflowService.GeneratePeerRequirements(GetCurrentGenerationRequest());
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
            Title = LocalizationService.Get("text.gegenstellenanforderungen_speichern"),
            Filter = "Textdatei (*.txt)|*.txt|Cisco-Konfiguration (*.cfg)|*.cfg|Alle Dateien (*.*)|*.*",
            FileName = "gegenstelle_anforderungen.txt"
        };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, _peerRequirementsText, new UTF8Encoding(false));
    }

    private void BuildImportConfigTab()
    {
        var tab = new TabItem { Header = LocalizationService.Get("tab.import") };
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
        importInfoStack.Children.Add(new TextBlock { Text = LocalizationService.Get("text.cisco_konfiguration_importieren"), FontSize = 19, FontWeight = FontWeights.Bold });
        importInfoStack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("text.komplette_running_config_oder_teilkonfiguration_einfugen_bek"),
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });
        importInfo.Child = importInfoStack;
        Grid.SetRow(importInfo, 0);
        left.Children.Add(importInfo);

        var importButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var loadButton = new Button { Content = LocalizationService.Get("import.load_config"), ToolTip = LocalizationService.Get("text.ladt_eine_vorhandene_txt_cfg_konfiguration_in_das_importfeld") };
        var analyzeButton = new Button { Content = LocalizationService.Get("common.analyze"), Style = TryFindResource("PrimaryButtonStyle") as Style, ToolTip = LocalizationService.Get("text.analysiert_komplette_konfigurationen_bekannte_werte_werden_e") };
        var applyButton = new Button { Content = LocalizationService.Get("text.daten_ubernehmen"), Style = TryFindResource("PrimaryButtonStyle") as Style, ToolTip = LocalizationService.Get("text.ubernimmt_erkannte_werte_in_die_vorhandenen_module_und_aktiv") };
        var exportUnknownButton = new Button { Content = LocalizationService.Get("import.export_unknown"), ToolTip = LocalizationService.Get("import.export_unknown_tooltip") };

        loadButton.Click += async (_, _) => await LoadConfigForImportAsync();
        analyzeButton.Click += (_, _) => AnalyzeImportedConfig();
        applyButton.Click += (_, _) => ApplyImportedConfig();
        exportUnknownButton.Click += async (_, _) => await ExportUnknownImportedCommandsAsync();

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
            Text = LocalizationService.Get("text.running_config_hier_einfugen_hostname_r1_interface_gigabitet")
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
        resultInfoStack.Children.Add(new TextBlock { Text = LocalizationService.Get("import.result"), FontSize = 19, FontWeight = FontWeights.Bold });
        resultInfoStack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("text.rechts_stehen_zusammenfassung_ubernehmbare_felder_aktivierte"),
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });
        resultInfo.Child = resultInfoStack;
        Grid.SetRow(resultInfo, 0);
        right.Children.Add(resultInfo);

        var resultButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var copyUnknownButton = new Button { Content = LocalizationService.Get("import.copy_unknown"), ToolTip = LocalizationService.Get("text.kopiert_die_unbekannten_befehle_in_die_zwischenablage") };
        var copySummaryButton = new Button { Content = LocalizationService.Get("import.copy_summary"), ToolTip = LocalizationService.Get("text.kopiert_das_gesamte_import_ergebnis_in_die_zwischenablage") };
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
            Text = LocalizationService.Get("text.noch_keine_analyse_ausgefuhrt")
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

    private async Task LoadConfigForImportAsync(CancellationToken cancellationToken = default)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("text.cisco_konfiguration_laden"),
            Filter = LocalizationService.Get("text.konfiguration_txt_cfg_conf_txt_cfg_conf_alle_dateien")
        };
        if (dialog.ShowDialog(this) != true || _importConfigBox == null) return;

        try
        {
            _importConfigBox.Text = await _importWorkflowService.LoadConfigurationFileAsync(dialog.FileName, cancellationToken);
            AnalyzeImportedConfig();
        }
        catch (OperationCanceledException)
        {
            ValidationTextBlock.Text = LocalizationService.Get("import.workflow.cancelled");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.Get("import.workflow.load_error_title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AnalyzeImportedConfig()
    {
        if (_importConfigBox == null || _importResultBox == null) return;

        var moduleTitles = ModuleCatalog.All.ToDictionary(
            module => module.Name,
            module => module.Title,
            StringComparer.OrdinalIgnoreCase);
        _lastImportResult = _importWorkflowService.Analyze(
            _importConfigBox.Text,
            moduleTitles,
            GetImportWorkflowText());
        _importResultBox.Text = _lastImportResult.Preview;

        ValidationTextBlock.Text = _lastImportResult.HasUnknownCommands
            ? LocalizationService.Format("import.workflow.status_with_unknown", _lastImportResult.Analysis.AppliedFields, _lastImportResult.Analysis.UnknownCommands.Count)
            : LocalizationService.Format("import.workflow.status_success", _lastImportResult.Analysis.AppliedFields);
        ValidationTextBlock.Foreground = _lastImportResult.HasUnknownCommands
            ? new SolidColorBrush(Color.FromRgb(251, 191, 36))
            : new SolidColorBrush(Color.FromRgb(134, 239, 172));
    }

    private void ApplyImportedConfig()
    {
        if (_lastImportResult == null) AnalyzeImportedConfig();
        if (_lastImportResult == null) return;

        var plan = _importWorkflowService.CreateApplicationPlan(_lastImportResult, ModuleCatalog.All);
        ResetControlsForImportedFields(plan.FieldsToReset);

        foreach (var pair in plan.Values)
            SetControlValue(pair.Key, pair.Value);

        foreach (var check in _moduleChecks.Values)
            check.IsChecked = false;

        foreach (var pair in plan.Modules)
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
            LocalizationService.Format(
                "import.workflow.applied_body",
                _lastImportResult.Analysis.AppliedFields,
                _lastImportResult.Analysis.ActiveModules,
                _lastImportResult.Analysis.UnknownCommands.Count),
            LocalizationService.Get("import.workflow.applied_title"),
            MessageBoxButton.OK,
            _lastImportResult.HasUnknownCommands ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void ResetControlsForImportedFields(IReadOnlySet<string> fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            if (!_valueControls.TryGetValue(fieldName, out var control)) continue;

            if (control is TextBox textBox)
            {
                textBox.Text = string.Empty;
                continue;
            }

            if (control is ComboBox comboBox)
            {
                var neutral = comboBox.Items.Cast<object>()
                    .FirstOrDefault(item => string.IsNullOrWhiteSpace(item?.ToString()))
                    ?? comboBox.Items.Cast<object>()
                        .FirstOrDefault(item =>
                            string.Equals(item?.ToString(), "Nein", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item?.ToString(), "No", StringComparison.OrdinalIgnoreCase))
                    ?? comboBox.Items.Cast<object>().FirstOrDefault();
                comboBox.SelectedItem = neutral;
            }
        }
    }

    private async Task ExportUnknownImportedCommandsAsync(CancellationToken cancellationToken = default)
    {
        if (_lastImportResult == null) AnalyzeImportedConfig();
        if (_lastImportResult == null) return;

        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.Get("text.unbekannte_importbefehle_speichern"),
            Filter = LocalizationService.Get("dialog.filter.text_all"),
            FileName = LocalizationService.Get("import.workflow.unknown_filename")
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            await _importWorkflowService.ExportUnknownCommandsAsync(dialog.FileName, _lastImportResult, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ValidationTextBlock.Text = LocalizationService.Get("import.workflow.cancelled");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.Get("import.workflow.export_error_title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyUnknownImportedCommands()
    {
        if (_lastImportResult == null) AnalyzeImportedConfig();
        if (_lastImportResult == null) return;
        Clipboard.SetText(_importWorkflowService.GetUnknownCommandsCopyText(_lastImportResult));
    }





    private static ImportWorkflowText GetImportWorkflowText() => new()
    {
        SummaryHeading = LocalizationService.Get("import.workflow.summary_heading"),
        TotalCommands = LocalizationService.Get("import.workflow.total_commands"),
        KnownCommands = LocalizationService.Get("import.workflow.known_commands"),
        ApplicableFields = LocalizationService.Get("import.workflow.applicable_fields"),
        ApplicableModules = LocalizationService.Get("import.workflow.applicable_modules"),
        UnknownCommands = LocalizationService.Get("import.workflow.unknown_commands"),
        NotesHeading = LocalizationService.Get("import.workflow.notes_heading"),
        EmptyConfigurationNote = LocalizationService.Get("import.workflow.note_empty"),
        UnknownCommandsNoteFormat = LocalizationService.Get("import.workflow.note_unknown"),
        NoApplicableFieldsNote = LocalizationService.Get("import.workflow.note_no_fields"),
        RecognizedModulesHeading = LocalizationService.Get("import.workflow.recognized_modules_heading"),
        ApplicableFieldsHeading = LocalizationService.Get("import.workflow.applicable_fields_heading"),
        UnknownCommandsHeading = LocalizationService.Get("import.workflow.unknown_commands_heading"),
        NoUnknownCommands = LocalizationService.Get("import.workflow.no_unknown_commands"),
        UnknownCommandLineFormat = LocalizationService.Get("import.workflow.unknown_line_format"),
        MoreUnknownCommandsFormat = LocalizationService.Get("import.workflow.more_unknown"),
        UnknownExportHeading = LocalizationService.Get("import.workflow.unknown_export_heading"),
        CountLabel = LocalizationService.Get("import.workflow.count")
    };

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
        var tab = new TabItem { Header = LocalizationService.Get("tab.command_registry") };
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
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        top.Child = topGrid;

        var info = new StackPanel();
        info.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("navigation.command_registry"),
            FontSize = 19,
            FontWeight = FontWeights.Bold
        });
        info.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("text.aktualisierte_cisco_konfigurations_diagnose_pruf_und_wartung"),
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            FontSize = 12,
            Margin = new Thickness(0, 3, 0, 0)
        });
        topGrid.Children.Add(info);

        var groupPanel = new StackPanel();
        groupPanel.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("text.bereich"),
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var groupSelector = new ComboBox
        {
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ToolTip = TooltipBuilder.Create(LocalizationService.Get("text.bereich_auswahlen"), LocalizationService.Get("text.zeigt_alle_befehle_oder_nur_die_befehle_einer_bestimmten_gru"))
        };
        groupSelector.Items.Add("Alle Bereiche");
        foreach (var group in commandGroups)
            groupSelector.Items.Add(group.Name);
        groupSelector.ItemTemplate = LocalizationService.CreateLocalizedStringTemplate();
        groupSelector.SelectedIndex = 0;
        groupPanel.Children.Add(groupSelector);
        Grid.SetColumn(groupPanel, 1);
        topGrid.Children.Add(groupPanel);

        var searchPanel = new StackPanel();
        searchPanel.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("text.suche"),
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var searchBox = new TextBox
        {
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            ToolTip = TooltipBuilder.Create(LocalizationService.Get("text.suche"), LocalizationService.Get("text.durchsucht_modul_modus_befehl_und_beschreibung_beispiele_osp"))
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
            Header = LocalizationService.Get("text.modul"),
            Binding = new System.Windows.Data.Binding("Module") { Converter = new LocalizedTextConverter() },
            Width = new DataGridLength(150),
            ElementStyle = wrapStyle
        });
        commandGrid.Columns.Add(new DataGridTextColumn
        {
            Header = LocalizationService.Get("header.mode"),
            Binding = new System.Windows.Data.Binding("Mode") { Converter = new LocalizedTextConverter() },
            Width = new DataGridLength(135),
            ElementStyle = wrapStyle
        });
        commandGrid.Columns.Add(new DataGridTextColumn
        {
            Header = LocalizationService.Get("text.befehl_syntax"),
            Binding = new System.Windows.Data.Binding("Command"),
            Width = new DataGridLength(430),
            ElementStyle = wrapStyle
        });
        commandGrid.Columns.Add(new DataGridTextColumn
        {
            Header = LocalizationService.Get("text.beschreibung"),
            Binding = new System.Windows.Data.Binding("Meaning") { Converter = new CommandDescriptionConverter() },
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
                    Contains(r.Meaning, q) ||
                    Contains(LocalizationService.TranslateText(r.Module), q) ||
                    Contains(LocalizationService.TranslateText(r.Mode), q) ||
                    Contains(LocalizationService.TranslateNaturalLanguageText(r.Meaning), q));
            }

            var filtered = rows
                .GroupBy(r => $"{r.Module}{r.Mode}{r.Command}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(r => r.Module, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Command, StringComparer.OrdinalIgnoreCase)
                .ToList();
            commandGrid.ItemsSource = filtered;
            var selectedArea = groupSelector.SelectedItem?.ToString() ?? "Alle Bereiche";
            resultText.Text = LocalizationService.IsEnglish
                ? $"{LocalizationService.TranslateText(selectedArea)} · {filtered.Count} commands"
                : $"{selectedArea} · {filtered.Count} Befehle";
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
        var tab = new TabItem { Header = LocalizationService.Get("tab.subnetting") };
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
            Text = LocalizationService.Get("text.subnetting_subnetzrechner"),
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("text.unterstutzt_ipv4_und_ipv6_automatisch_anhand_der_eingegebene"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(174, 184, 197)),
            Margin = new Thickness(0, 0, 0, 14)
        });

        var network = AddCheckField(stack, "Netz / CIDR (IPv4 oder IPv6)", "192.168.10.0/24");
        var newPrefix = AddCheckField(stack, "Neuer Präfix", "26");
        var maxSubnets = AddCheckField(stack, "Max. Subnetze anzeigen", "16");

        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("text.ipv6_beispiel_2001_db8_10_48_mit_neuem_prafix_64"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, -4, 0, 10)
        });

        var calculateButton = new Button
        {
            Content = LocalizationService.Get("text.subnetze_berechnen"),
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
            Text = LocalizationService.Get("text.einzel_ip_prufen"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("text.pruft_ipv4_oder_ipv6_berechnet_das_zugehorige_netz_und_zeigt"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(174, 184, 197)),
            Margin = new Thickness(0, 0, 0, 12)
        });

        var ip = AddCheckField(stack, "IP-Adresse (IPv4 oder IPv6)", "192.168.10.25");
        var mask = AddCheckField(stack, "Präfix / Subnetzmaske", "/24");
        var wildcard = AddCheckField(stack, "Wildcard optional (nur IPv4)", "");

        stack.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("text.ipv6_beispiel_2001_db8_10_25_mit_64_alternativ_kann_die_adre"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, -4, 0, 10)
        });

        var checkButton = new Button
        {
            Content = LocalizationService.Get("text.ip_prufen"),
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
            Text = LocalizationService.Get("text.ergebnis"),
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
            Text = LocalizationService.Get("text.bereit_ipv4_beispiel_netz_cidr_192_168_10_0_24_neuer_prafix_")
        };
        Grid.SetRow(output, 1);
        resultGrid.Children.Add(output);

        resultCard.Child = resultGrid;
        grid.Children.Add(resultCard);

        calculateButton.Click += (_, _) =>
        {
            SubnettingCalculator.TryCalculate(network.Text, newPrefix.Text, maxSubnets.Text, out var result);
            output.Text = LocalizationService.TranslateText(result);
            output.ScrollToHome();
        };

        checkButton.Click += (_, _) =>
        {
            IpCalculator.TryCalculate(ip.Text, mask.Text, wildcard.Text, out var result);
            output.Text = LocalizationService.TranslateText(result);
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
                activeText.Text = LocalizationService.IsEnglish ? $"{active} active" : $"{active} aktiv";
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
        badge.ToolTip = LocalizationService.TranslateText(tooltip);
        text.Text = LocalizationService.TranslateText(label);
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
            var request = GetCurrentGenerationRequest();
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
                var request = GetCurrentGenerationRequest();
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

        ValidationTextBlock.Text = LocalizationService.TranslateText(validationText);
        var statusOk = !hasWarnings && (!_duplicateCheckHasRun || _lastDuplicateConfigIssues.Count == 0);
        ValidationTextBlock.Foreground = statusOk
            ? new SolidColorBrush(Color.FromRgb(134, 239, 172))
            : new SolidColorBrush(Color.FromRgb(251, 191, 36));
        StatusDot.Foreground = ValidationTextBlock.Foreground;

        var active = ModuleCatalog.All.Count(m => IsAllowed(m) && _moduleChecks.TryGetValue(m.Name, out var cb) && cb.IsChecked == true);
        var available = ModuleCatalog.All.Count(IsAllowed);
        ModuleSummaryTextBlock.Text = LocalizationService.TranslateText($"{active} von {available} Modulen aktiv");
        if (_dashboardDeviceText != null)
            _dashboardDeviceText.Text = $"{LocalizationService.TranslateText(DeviceTypeCombo.SelectedItem?.ToString() ?? "Router")} · {LocalizationService.TranslateText(ConfigModeCombo.SelectedItem?.ToString() ?? "Ohne VRF")}";
        if (_dashboardActiveModulesText != null)
            _dashboardActiveModulesText.Text = $"{active} von {available}";
        if (_dashboardValidationText != null)
        {
            _dashboardValidationText.Text = LocalizationService.TranslateText(statusOk ? "Keine Warnungen" : $"{_currentValidationIssues.Count} Hinweis(e)");
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
            ValidationTextBlock.ToolTip = TooltipBuilder.Create(LocalizationService.Get("text.duplikate_erkannt"), LocalizationService.Get("text.offne_den_bereich_ausgabe_und_prufe_den_dort_angezeigten_dup"));
        }
        else
        {
            ValidationTextBlock.ToolTip = TooltipBuilder.Create(LocalizationService.Get("text.validierung"), LocalizationService.Get("text.fur_die_aktuell_aktivierten_module_wurden_keine_offensichtli"));
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
        "Übersicht" => LocalizationService.Get("navigation.overview", "Übersicht"),
        "Basis" => LocalizationService.Get("navigation.base", "Basis"),
        "Management" => "Management",
        "Interfaces" => "Interface / Ports",
        "Switching" => LocalizationService.Get("navigation.switching", "Switching"),
        "Routing" => LocalizationService.Get("navigation.routing", "Routing"),
        "IPv6/DHCP/ACL" => LocalizationService.Get("navigation.network_services", "Netzdienste"),
        "Security/WAN" => "Security/WAN",
        "Subnetting" => LocalizationService.Get("navigation.subnetting", "Subnetting"),
        "Befehlsregister" => LocalizationService.Get("navigation.command_registry", "Befehlsregister"),
        "Betrieb" => "SSH",
        "Import" => LocalizationService.Get("navigation.import", "Import"),
        "Gegenstelle" => LocalizationService.Get("navigation.peer", "Gegenstelle"),
        "Ausgabe" => LocalizationService.Get("navigation.output", "Ausgabe"),
        "Projekt" => LocalizationService.Get("navigation.project", "Projekt"),
        "IPAM / Ports" => "IPAM",
        "Analyse" => LocalizationService.Get("navigation.analysis", "Analyse"),
        "Diagramm / Bericht" => "Diagramm",
        "Plugin-Manager" => LocalizationService.Get("plugins.manager_title", "Plugin-Manager"),
        "Einstellungen" => LocalizationService.Get("navigation.settings", "Einstellungen"),
        _ => tab
    };

    private static string DescriptionForTab(string tab) => tab switch
    {
        "Basis" => LocalizationService.Get("module_tab.base.description", "Gerätegrunddaten, Zeiteinstellungen und Banner zentral festlegen."),
        "Management" => LocalizationService.Get("module_tab.management.description", "Administrationszugänge, Benutzer, AAA und Monitoring konfigurieren."),
        "Interfaces" => LocalizationService.Get("module_tab.interfaces.description", "Physische Ports, Ranges, Trunks, EtherChannel und Subinterfaces verwalten."),
        "Switching" => LocalizationService.Get("module_tab.switching.description", "VLANs, Spanning Tree, Access-Sicherheit und Layer-2-Schutzmechanismen."),
        "Routing" => LocalizationService.Get("module_tab.routing.description", "IPv4-Routing, Routingprotokolle, Redundanz, VRF und MPLS."),
        "IPv6/DHCP/ACL" => LocalizationService.Get("module_tab.services.description", "Adressvergabe, ACLs und IPv6-Funktionen übersichtlich zusammengeführt."),
        "Security/WAN" => LocalizationService.Get("module_tab.security.description", "Hardening, NAT, Firewalls, WAN-Redundanz und VPN-Technologien."),
        _ => LocalizationService.Get("module_tab.default.description", "Module dieses Konfigurationsbereichs auswählen und bearbeiten.")
    };

    private static string IconForTab(string tab) => tab switch
    {
        "Übersicht" => "⌂",
        "Basis" => "◆",
        "Management" => "⚙",
        "Interfaces" => "↔",
        "Switching" => "⇄",
        "Routing" => "◇",
        "Security/WAN" => "▣",
        "IPv6/DHCP/ACL" => "◎",
        "Subnetting" => "◫",
        "Befehlsregister" => "≡",
        "Betrieb" => "⌁",
        "Import" => "⇩",
        "Gegenstelle" => "⇆",
        "Ausgabe" => "▤",
        "Projekt" => "▰",
        "IPAM / Ports" => "▦",
        "Analyse" => "⌕",
        "Diagramm / Bericht" => "▧",
        "Plugin-Manager" => "⬢",
        "Einstellungen" => "⚙",
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


    private GenerationRequest GetCurrentGenerationRequest()
    {
        var selectedModules = ModuleCatalog.All.ToDictionary(
            module => module.Name,
            module => _moduleChecks.TryGetValue(module.Name, out var checkBox) && checkBox.IsChecked == true,
            StringComparer.OrdinalIgnoreCase);

        return _configurationWorkflowService.BuildRequest(CollectValues(), selectedModules, ModuleCatalog.All);
    }

    private ConfigurationWorkflowOptions GetConfigurationWorkflowOptions() => new()
    {
        IncludeComments = _appSettings.IncludeComments,
        IncludeSectionSeparators = _appSettings.IncludeSectionSeparators,
        IncludeEnable = _appSettings.IncludeEnable,
        IncludeConfigureTerminal = _appSettings.IncludeConfigureTerminal,
        IncludeEnd = _appSettings.IncludeEnd,
        IncludeWriteMemory = _appSettings.IncludeWriteMemory,
        LineEndings = _appSettings.LineEndings,
        ExportFileNamePattern = _appSettings.ExportFileNamePattern,
        TimestampInFileName = _appSettings.TimestampInFileName
    };

    private static ConfigurationPreviewText GetConfigurationPreviewText() => new()
    {
        Heading = LocalizationService.Get("configuration.workflow.duplicate.heading"),
        ResultFormat = LocalizationService.Get("configuration.workflow.duplicate.result"),
        Recommendation = LocalizationService.Get("configuration.workflow.duplicate.recommendation"),
        IssueFormat = LocalizationService.Get("configuration.workflow.duplicate.issue"),
        MoreIssuesFormat = LocalizationService.Get("configuration.workflow.duplicate.more")
    };

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
        _lastConfigurationWorkflowResult = null;
        _peerRequirementsText = string.Empty;
        if (_peerRequirementsBox != null)
            _peerRequirementsBox.Text = LocalizationService.Get("text.eingaben_wurden_geandert_klicke_auf_gegenstelle_aktualisiere");
    }



    private void ShowDuplicateWarningIfNeeded(string action)
    {
        if (!_duplicateCheckHasRun || _lastDuplicateConfigIssues.Count == 0) return;

        var issueText = _configurationWorkflowService.BuildDuplicateWarning(
            _lastDuplicateConfigIssues,
            LocalizationService.Get("configuration.workflow.duplicate.warning_issue"),
            LocalizationService.Get("configuration.workflow.duplicate.warning_more"));
        var message = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            LocalizationService.Get("configuration.workflow.duplicate.warning_body"),
            action,
            issueText);

        MessageBox.Show(this,
            message,
            LocalizationService.Get("configuration.workflow.duplicate.warning_title"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }










    private async Task<ConfigurationWorkflowResult> GenerateConfigurationWorkflowAsync(CancellationToken cancellationToken = default)
    {
        var request = GetCurrentGenerationRequest();
        var result = await _configurationWorkflowService.GenerateAsync(
            request,
            GetConfigurationWorkflowOptions(),
            GetConfigurationPreviewText(),
            cancellationToken);
        _lastConfigurationWorkflowResult = result;
        _lastDuplicateConfigIssues = result.DuplicateIssues;
        _duplicateCheckHasRun = true;
        UpdateStatusBar();
        return result;
    }

    private async Task<string> GenerateConfigAsync(CancellationToken cancellationToken = default) =>
        (await GenerateConfigurationWorkflowAsync(cancellationToken)).Configuration;

    private async Task CopyGeneratedConfigAsync()
    {
        try
        {
            var result = await GenerateConfigurationWorkflowAsync();
            Clipboard.SetText(_configurationWorkflowService.GetCopyText(result, includePreview: false));
            ShowDuplicateWarningIfNeeded(LocalizationService.Get("configuration.workflow.action.copied"));
            MessageBox.Show(this, LocalizationService.Get("text.konfiguration_wurde_in_die_zwischenablage_kopiert"), LocalizationService.Get("text.kopiert"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.Get("text.fehler_beim_erzeugen"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SaveTextExportAsync()
    {
        var options = GetConfigurationWorkflowOptions();
        var values = CollectValues();
        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.Get("text.cisco_konfiguration_speichern"),
            Filter = LocalizationService.Get("dialog.filter.text_all"),
            FileName = _configurationWorkflowService.BuildSafeExportFileName(options, values, DateTime.Now)
        };
        if (!string.IsNullOrWhiteSpace(_appSettings.DefaultExportFolder) && Directory.Exists(_appSettings.DefaultExportFolder))
            dialog.InitialDirectory = _appSettings.DefaultExportFolder;
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var generation = await GenerateConfigurationWorkflowAsync();
            await _configurationWorkflowService.ExportAsync(new ConfigurationExportRequest
            {
                TargetPath = dialog.FileName,
                Generation = generation,
                ExportPeerConfiguration = _appSettings.ExportPeerConfiguration,
                RollbackText = _appSettings.GenerateRollbackFile ? _rollbackBox?.Text ?? string.Empty : string.Empty,
                ReportText = _appSettings.ExportReportsTogether ? _reportPreviewBox?.Text ?? string.Empty : string.Empty
            });

            ShowDuplicateWarningIfNeeded(LocalizationService.Get("configuration.workflow.action.exported"));
            MessageBox.Show(this, LocalizationService.Get("text.konfiguration_wurde_gespeichert"), LocalizationService.Get("text.export_abgeschlossen"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.Get("text.fehler_beim_export"), MessageBoxButton.OK, MessageBoxImage.Error);
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
            Title = LocalizationService.Get("text.com_port_testmodus_auswahlen"),
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
            Text = LocalizationService.Get("text.serielle_verbindung"),
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
        var ok = new Button { Content = LocalizationService.Get("common.apply"), MinWidth = 105 };
        var test = new Button { Content = LocalizationService.Get("common.test_mode"), MinWidth = 105 };
        var cancel = new Button { Content = LocalizationService.Get("common.cancel"), MinWidth = 95 };
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
                ? (LocalizationService.IsEnglish
                    ? $"COM test mode active · {_serialSettings.BaudRate} baud · delay {_serialSettings.LineDelayMs} ms · log file instead of a physical COM port"
                    : $"COM-Testmodus aktiv · {_serialSettings.BaudRate} Baud · Delay {_serialSettings.LineDelayMs} ms · Logdatei statt echter COM-Port")
                : (LocalizationService.IsEnglish
                    ? $"COM port selected: {_serialSettings.PortName} · {_serialSettings.BaudRate} baud · delay {_serialSettings.LineDelayMs} ms"
                    : $"COM-Port gewählt: {_serialSettings.PortName} · {_serialSettings.BaudRate} Baud · Delay {_serialSettings.LineDelayMs} ms");
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
        ValidationTextBlock.Text = LocalizationService.IsEnglish
            ? $"COM test mode active · {_serialSettings.BaudRate} baud · delay {_serialSettings.LineDelayMs} ms · log file instead of a physical COM port"
            : $"COM-Testmodus aktiv · {_serialSettings.BaudRate} Baud · Delay {_serialSettings.LineDelayMs} ms · Logdatei statt echter COM-Port";
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
            MessageBox.Show(this, ex.Message, LocalizationService.Get("text.com_fehler"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetAllInputs()
    {
        if (_appSettings.ConfirmReset)
        {
            var result = MessageBox.Show(
                this,
                LocalizationService.Get("text.sollen_wirklich_alle_eingaben_aktivierten_module_importdaten"),
                LocalizationService.Get("header.reset_all"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }

        DeviceTypeCombo.SelectedItem = _appSettings.DefaultDeviceType;
        ConfigModeCombo.SelectedItem = _appSettings.DefaultConfigMode;
        WriteMemCombo.SelectedItem = _appSettings.IncludeWriteMemory ? "Ja" : "Nein";

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
                    checkBox.IsChecked = string.Equals(field.Value, "Ja", StringComparison.OrdinalIgnoreCase);
            }
        }

        _lastImportResult = null;
        if (_importConfigBox != null) _importConfigBox.Clear();
        if (_importResultBox != null) _importResultBox.Clear();
        _lastDuplicateConfigIssues = Array.Empty<DuplicateConfigIssue>();
        _duplicateCheckHasRun = false;
        _currentValidationIssues = Array.Empty<UiValidationIssue>();
        _configurationPreviewText = string.Empty;
        SetConfigurationPreviewText(LocalizationService.Get("configuration.workflow.preview.initial"));
        _peerRequirementsText = string.Empty;
        if (_peerRequirementsBox != null)
            _peerRequirementsBox.Text = LocalizationService.Get("text.klicke_auf_gegenstelle_aktualisieren_um_die_anforderungen_de");

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
            Title = LocalizationService.Get("text.vorlage_speichern"),
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
            Title = LocalizationService.Get("header.load_template"),
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
