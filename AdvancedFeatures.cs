using Microsoft.Win32;
using System.IO;
using IOPath = System.IO.Path;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CiscoConfigGuiWpf;

public partial class MainWindow
{
    private NetworkProject _currentProject = new();
    private string _currentProjectPath = string.Empty;
    private TextBox? _projectNameBox;
    private TextBox? _projectDescriptionBox;
    private DataGrid? _projectDeviceGrid;
    private DataGrid? _ipamGrid;
    private DataGrid? _portPlanGrid;
    private DataGrid? _dependencyGrid;
    private DataGrid? _securityGrid;
    private DataGrid? _diffGrid;
    private DataGrid? _searchGrid;
    private DataGrid? _backupGrid;
    private TextBox? _diffOldBox;
    private TextBox? _diffNewBox;
    private TextBox? _rollbackBox;
    private TextBox? _commandInputBox;
    private TextBox? _commandAnalysisBox;
    private TextBox? _searchBox;
    private TextBox? _operationsOutputBox;
    private TextBox? _sshHostBox;
    private TextBox? _sshPortBox;
    private TextBox? _sshUserBox;
    private ComboBox? _sshAuthModeCombo;
    private TextBox? _sshKeyBox;
    private PasswordBox? _sshPasswordBox;
    private TextBox? _sshDelayBox;
    private CheckBox? _sshSaveCheck;
    private Canvas? _diagramCanvas;
    private ComboBox? _linkSourceCombo;
    private ComboBox? _linkTargetCombo;
    private TextBox? _linkSourceIfBox;
    private TextBox? _linkTargetIfBox;
    private ComboBox? _linkTypeCombo;
    private TextBox? _reportPreviewBox;
    private DispatcherTimer? _autoSaveTimer;
    private readonly Dictionary<string, TextBox> _moduleLivePreviewBoxes = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<DependencyFinding> _advancedDependencyFindings = Array.Empty<DependencyFinding>();
    private IReadOnlyList<SecurityFinding> _advancedSecurityFindings = Array.Empty<SecurityFinding>();
    private IReadOnlyList<ConfigDiffLine> _advancedDiff = Array.Empty<ConfigDiffLine>();

    private void BuildAdvancedFeatureTabs()
    {
        BuildProjectWorkspaceTab();
        BuildIpamAndPortPlanTab();
        BuildAnalysisCenterTab();
        BuildOperationsTab();
        BuildDiagramAndReportTab();
        InitializeAutoSave();
    }

    private void BuildProjectWorkspaceTab()
    {
        var tab = new TabItem { Header = "▣  Projekt" };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = CreateAdvancedHeader(
            "Mehrgeräte-Projekt",
            "Verwaltet mehrere Router und Switches mit gemeinsamen IPAM-, Link-, Backup- und Berichtsdaten.");
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var newButton = new Button { Content = "Neu" };
        var openButton = new Button { Content = "Öffnen" };
        var saveButton = new Button { Content = "Speichern", Style = TryFindResource("PrimaryButtonStyle") as Style };
        newButton.Click += (_, _) => NewNetworkProject();
        openButton.Click += (_, _) => OpenNetworkProject();
        saveButton.Click += (_, _) => SaveNetworkProject(false);
        headerActions.Children.Add(newButton);
        headerActions.Children.Add(openButton);
        headerActions.Children.Add(saveButton);
        AddAdvancedHeaderActions(header, headerActions);
        root.Children.Add(header);

        var content = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        var detailsCard = CreateAdvancedCard();
        var details = new StackPanel();
        details.Children.Add(AdvancedTitle("Projektinformationen"));
        _projectNameBox = AddAdvancedTextField(details, "Projektname", _currentProject.Name);
        _projectDescriptionBox = AddAdvancedTextField(details, "Beschreibung", _currentProject.Description, true, 120);
        _projectNameBox.TextChanged += (_, _) => ScheduleAutoSave();
        _projectDescriptionBox.TextChanged += (_, _) => ScheduleAutoSave();
        details.Children.Add(AdvancedNote("Beim Speichern werden Geräte, IPAM, Verbindungen und Backups gemeinsam in einer .ciscoproject.json-Datei abgelegt."));
        detailsCard.Child = details;
        content.Children.Add(detailsCard);

        var deviceArea = new Grid();
        deviceArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        deviceArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(deviceArea, 2);
        content.Children.Add(deviceArea);

        var deviceButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var captureButton = new Button { Content = "Aktuelles Gerät übernehmen", Style = TryFindResource("PrimaryButtonStyle") as Style };
        var updateButton = new Button { Content = "Ausgewähltes aktualisieren" };
        var applyButton = new Button { Content = "Ausgewähltes laden" };
        var duplicateButton = new Button { Content = "Duplizieren" };
        var peerButton = new Button { Content = "Gegenstelle erzeugen" };
        var showConfigButton = new Button { Content = "Konfiguration anzeigen" };
        var exportConfigButton = new Button { Content = "Konfiguration exportieren" };
        var deleteButton = new Button { Content = "Entfernen" };
        captureButton.Click += async (_, _) => await CaptureCurrentDeviceAsync(false);
        updateButton.Click += async (_, _) => await UpdateSelectedProjectDeviceAsync();
        applyButton.Click += (_, _) => ApplySelectedProjectDevice();
        duplicateButton.Click += (_, _) => DuplicateSelectedProjectDevice();
        peerButton.Click += async (_, _) => await CreatePeerProjectDeviceAsync();
        showConfigButton.Click += (_, _) => ShowSelectedProjectDeviceConfiguration();
        exportConfigButton.Click += (_, _) => ExportSelectedProjectDeviceConfiguration();
        deleteButton.Click += (_, _) => DeleteSelectedProjectDevice();
        foreach (var b in new[] { captureButton, updateButton, applyButton, duplicateButton, peerButton, showConfigButton, exportConfigButton, deleteButton }) deviceButtons.Children.Add(b);
        deviceArea.Children.Add(deviceButtons);

        _projectDeviceGrid = new DataGrid
        {
            ItemsSource = _currentProject.Devices,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            AutoGenerateColumns = false
        };
        _projectDeviceGrid.Columns.Add(new DataGridTextColumn { Header = "Gerät", Binding = new Binding(nameof(ProjectDeviceSnapshot.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _projectDeviceGrid.Columns.Add(new DataGridTextColumn { Header = "Typ", Binding = new Binding(nameof(ProjectDeviceSnapshot.DeviceType)), Width = 115 });
        _projectDeviceGrid.Columns.Add(new DataGridTextColumn { Header = "Modus", Binding = new Binding(nameof(ProjectDeviceSnapshot.ConfigMode)), Width = 120 });
        _projectDeviceGrid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding(nameof(ProjectDeviceSnapshot.Status)), Width = 165 });
        _projectDeviceGrid.Columns.Add(new DataGridTextColumn { Header = "Geändert", Binding = new Binding(nameof(ProjectDeviceSnapshot.LastUpdatedUtc)) { StringFormat = "dd.MM.yyyy HH:mm" }, Width = 145 });
        _projectDeviceGrid.MouseDoubleClick += (_, _) => ApplySelectedProjectDevice();
        Grid.SetRow(_projectDeviceGrid, 1);
        deviceArea.Children.Add(_projectDeviceGrid);

        tab.Content = root;
        _tabsByName["Projekt"] = tab;
        MainTabs.Items.Add(tab);
    }

    private void BuildIpamAndPortPlanTab()
    {
        var tab = new TabItem { Header = "▤  IPAM / Ports" };
        var inner = new TabControl();
        inner.Items.Add(BuildIpamSubTab());
        inner.Items.Add(BuildPortPlanSubTab());
        tab.Content = inner;
        _tabsByName["IPAM / Ports"] = tab;
        MainTabs.Items.Add(tab);
    }

    private TabItem BuildIpamSubTab()
    {
        var tab = new TabItem { Header = "IP-Adressverwaltung" };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        root.Children.Add(CreateAdvancedHeader("IPAM", "Zentrale IPv4-/IPv6-Netzplanung mit VLAN, Gateway, DHCP-Bereich, Gerät und Interface."));
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        var add = new Button { Content = "Eintrag hinzufügen", Style = TryFindResource("PrimaryButtonStyle") as Style };
        var remove = new Button { Content = "Ausgewählten entfernen" };
        var importCurrent = new Button { Content = "Aus aktueller Konfiguration" };
        var importProject = new Button { Content = "Aus allen Projektgeräten" };
        var validate = new Button { Content = "Überschneidungen prüfen" };
        var export = new Button { Content = "CSV Export" };
        add.Click += (_, _) => { _currentProject.IpamEntries.Add(new IpamEntry()); _ipamGrid?.ScrollIntoView(_currentProject.IpamEntries.Last()); ScheduleAutoSave(); };
        remove.Click += (_, _) => { if (_ipamGrid?.SelectedItem is IpamEntry item) _currentProject.IpamEntries.Remove(item); ScheduleAutoSave(); };
        importCurrent.Click += async (_, _) => await ImportCurrentConfigIntoIpamAsync();
        importProject.Click += (_, _) => ImportProjectConfigsIntoIpam();
        validate.Click += (_, _) => ShowIpamValidation();
        export.Click += (_, _) => ExportIpamCsv();
        foreach (var b in new[] { add, remove, importCurrent, importProject, validate, export }) actions.Children.Add(b);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);

        _ipamGrid = new DataGrid
        {
            ItemsSource = _currentProject.IpamEntries,
            IsReadOnly = false,
            CanUserAddRows = true,
            CanUserDeleteRows = true,
            AutoGenerateColumns = false
        };
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = "Netzadresse", Binding = new Binding(nameof(IpamEntry.Network)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 145 });
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = "Präfix", Binding = new Binding(nameof(IpamEntry.PrefixLength)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 65 });
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = "VLAN", Binding = new Binding(nameof(IpamEntry.Vlan)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 65 });
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = "Gateway", Binding = new Binding(nameof(IpamEntry.Gateway)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 145 });
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = "DHCP Start", Binding = new Binding(nameof(IpamEntry.DhcpStart)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 145 });
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = "DHCP Ende", Binding = new Binding(nameof(IpamEntry.DhcpEnd)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 145 });
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = "Gerät", Binding = new Binding(nameof(IpamEntry.Device)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 130 });
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = "Interface", Binding = new Binding(nameof(IpamEntry.Interface)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 150 });
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = "Beschreibung", Binding = new Binding(nameof(IpamEntry.Description)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _ipamGrid.CellEditEnding += (_, _) => ScheduleAutoSave();
        Grid.SetRow(_ipamGrid, 2);
        root.Children.Add(_ipamGrid);
        tab.Content = root;
        return tab;
    }

    private TabItem BuildPortPlanSubTab()
    {
        var tab = new TabItem { Header = "Portplan" };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader("Interface- und Portplan", "Erzeugt aus der aktuellen oder gespeicherten Konfiguration eine tabellarische Portübersicht."));
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        var current = new Button { Content = "Aktuelle Konfiguration", Style = TryFindResource("PrimaryButtonStyle") as Style };
        var selected = new Button { Content = "Ausgewähltes Projektgerät" };
        var export = new Button { Content = "CSV Export" };
        current.Click += async (_, _) => await RefreshPortPlanFromCurrentAsync();
        selected.Click += (_, _) => RefreshPortPlanFromSelectedDevice();
        export.Click += (_, _) => ExportPortPlanCsv();
        actions.Children.Add(current); actions.Children.Add(selected); actions.Children.Add(export);
        Grid.SetRow(actions, 1); root.Children.Add(actions);

        _portPlanGrid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false };
        AddPortPlanColumns(_portPlanGrid);
        Grid.SetRow(_portPlanGrid, 2); root.Children.Add(_portPlanGrid);
        tab.Content = root;
        return tab;
    }

    private void BuildAnalysisCenterTab()
    {
        var tab = new TabItem { Header = "◉  Analyse" };
        var inner = new TabControl();
        inner.Items.Add(BuildDependencySubTab());
        inner.Items.Add(BuildSecuritySubTab());
        inner.Items.Add(BuildDiffSubTab());
        inner.Items.Add(BuildGlobalSearchSubTab());
        inner.Items.Add(BuildCommandAnalysisSubTab());
        tab.Content = inner;
        _tabsByName["Analyse"] = tab;
        MainTabs.Items.Add(tab);
    }

    private TabItem BuildDependencySubTab()
    {
        var tab = new TabItem { Header = "Abhängigkeiten" };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader("Abhängigkeits- und Konfliktprüfung", "Prüft Modulbeziehungen, fehlende Pflichtwerte, VLAN-/Trunk-Konsistenz und Routingvoraussetzungen."));
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        var refresh = new Button { Content = "Prüfen", Style = TryFindResource("PrimaryButtonStyle") as Style };
        var fix = new Button { Content = "Alle automatisch korrigierbaren beheben" };
        var navigate = new Button { Content = "Zum ausgewählten Feld" };
        refresh.Click += (_, _) => RefreshAdvancedDependencies();
        fix.Click += (_, _) => AutoFixAdvancedDependencies();
        navigate.Click += (_, _) => NavigateToSelectedDependency();
        actions.Children.Add(refresh); actions.Children.Add(fix); actions.Children.Add(navigate);
        Grid.SetRow(actions, 1); root.Children.Add(actions);

        _dependencyGrid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false };
        _dependencyGrid.Columns.Add(new DataGridTextColumn { Header = "Stufe", Binding = new Binding(nameof(DependencyFinding.Severity)), Width = 85 });
        _dependencyGrid.Columns.Add(new DataGridTextColumn { Header = "Bereich", Binding = new Binding(nameof(DependencyFinding.Area)), Width = 115 });
        _dependencyGrid.Columns.Add(new DataGridTextColumn { Header = "Meldung", Binding = new Binding(nameof(DependencyFinding.Message)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _dependencyGrid.Columns.Add(new DataGridTextColumn { Header = "Auto-Fix", Binding = new Binding(nameof(DependencyFinding.FixKey)), Width = 180 });
        _dependencyGrid.MouseDoubleClick += (_, _) => NavigateToSelectedDependency();
        Grid.SetRow(_dependencyGrid, 2); root.Children.Add(_dependencyGrid);
        tab.Content = root;
        return tab;
    }

    private TabItem BuildSecuritySubTab()
    {
        var tab = new TabItem { Header = "Sicherheit" };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader("Sicherheitsprüfung", "Analysiert die erzeugte Konfiguration auf typische Management-, Passwort-, SNMP-, STP- und VLAN-Risiken."));
        var refresh = new Button { Content = "Aktuelle Konfiguration prüfen", Style = TryFindResource("PrimaryButtonStyle") as Style, Margin = new Thickness(0, 8, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        refresh.Click += async (_, _) => await RefreshSecurityAuditAsync();
        Grid.SetRow(refresh, 1); root.Children.Add(refresh);
        _securityGrid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false };
        _securityGrid.Columns.Add(new DataGridTextColumn { Header = "Stufe", Binding = new Binding(nameof(SecurityFinding.Severity)), Width = 85 });
        _securityGrid.Columns.Add(new DataGridTextColumn { Header = "Kategorie", Binding = new Binding(nameof(SecurityFinding.Category)), Width = 120 });
        _securityGrid.Columns.Add(new DataGridTextColumn { Header = "Feststellung", Binding = new Binding(nameof(SecurityFinding.Message)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _securityGrid.Columns.Add(new DataGridTextColumn { Header = "Empfehlung", Binding = new Binding(nameof(SecurityFinding.Recommendation)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.SetRow(_securityGrid, 2); root.Children.Add(_securityGrid);
        tab.Content = root;
        return tab;
    }

    private TabItem BuildDiffSubTab()
    {
        var tab = new TabItem { Header = "Vergleich / Rollback" };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader("Konfigurationsvergleich", "Vergleicht zwei Konfigurationen kontextbezogen und erzeugt eine Rücksetzkonfiguration."));

        var inputs = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        inputs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        inputs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _diffOldBox = CreateCodeBox("Alte Konfiguration");
        _diffNewBox = CreateCodeBox("Neue Konfiguration");
        inputs.Children.Add(_diffOldBox); Grid.SetColumn(_diffNewBox, 2); inputs.Children.Add(_diffNewBox);
        Grid.SetRow(inputs, 1); root.Children.Add(inputs);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var oldFile = new Button { Content = "Alt aus Datei" };
        var newFile = new Button { Content = "Neu aus Datei" };
        var currentAsNew = new Button { Content = "Aktuell als Neu" };
        var selectedAsOld = new Button { Content = "Projektgerät als Alt" };
        var compare = new Button { Content = "Vergleichen", Style = TryFindResource("PrimaryButtonStyle") as Style };
        oldFile.Click += (_, _) => LoadTextIntoBox(_diffOldBox);
        newFile.Click += (_, _) => LoadTextIntoBox(_diffNewBox);
        currentAsNew.Click += async (_, _) => { if (_diffNewBox != null) _diffNewBox.Text = await GenerateConfigAsync(); };
        selectedAsOld.Click += (_, _) => { if (_diffOldBox != null && _projectDeviceGrid?.SelectedItem is ProjectDeviceSnapshot d) _diffOldBox.Text = d.GeneratedConfiguration; };
        compare.Click += (_, _) => RefreshConfigDiff();
        foreach (var b in new[] { oldFile, newFile, currentAsNew, selectedAsOld, compare }) actions.Children.Add(b);
        Grid.SetRow(actions, 2); root.Children.Add(actions);

        _diffGrid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false };
        _diffGrid.Columns.Add(new DataGridTextColumn { Header = "Änderung", Binding = new Binding(nameof(ConfigDiffLine.Change)), Width = 100 });
        _diffGrid.Columns.Add(new DataGridTextColumn { Header = "Kontext", Binding = new Binding(nameof(ConfigDiffLine.Context)), Width = 220 });
        _diffGrid.Columns.Add(new DataGridTextColumn { Header = "Befehl", Binding = new Binding(nameof(ConfigDiffLine.Line)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _diffGrid.Columns.Add(new DataGridTextColumn { Header = "Alt", Binding = new Binding(nameof(ConfigDiffLine.OldLine)), Width = 55 });
        _diffGrid.Columns.Add(new DataGridTextColumn { Header = "Neu", Binding = new Binding(nameof(ConfigDiffLine.NewLine)), Width = 55 });
        Grid.SetRow(_diffGrid, 3); root.Children.Add(_diffGrid);
        _rollbackBox = CreateCodeBox("Rollback");
        Grid.SetRow(_rollbackBox, 4); root.Children.Add(_rollbackBox);
        tab.Content = root;
        return tab;
    }

    private TabItem BuildGlobalSearchSubTab()
    {
        var tab = new TabItem { Header = "Globale Suche" };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader("Suche über Module, Felder und Befehle", "Findet Einstellungen und Cisco-Befehle und navigiert direkt zum passenden Eingabefeld."));
        var searchRow = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        var button = new Button { Content = "Suchen", Style = TryFindResource("PrimaryButtonStyle") as Style };
        DockPanel.SetDock(button, Dock.Right);
        _searchBox = new TextBox { MinWidth = 420, Margin = new Thickness(0, 0, 8, 0) };
        _searchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) RefreshGlobalSearch(); };
        button.Click += (_, _) => RefreshGlobalSearch();
        searchRow.Children.Add(button); searchRow.Children.Add(_searchBox);
        Grid.SetRow(searchRow, 1); root.Children.Add(searchRow);
        _searchGrid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false };
        _searchGrid.Columns.Add(new DataGridTextColumn { Header = "Typ", Binding = new Binding(nameof(GlobalSearchResult.Kind)), Width = 85 });
        _searchGrid.Columns.Add(new DataGridTextColumn { Header = "Treffer", Binding = new Binding(nameof(GlobalSearchResult.Title)), Width = 300 });
        _searchGrid.Columns.Add(new DataGridTextColumn { Header = "Beschreibung", Binding = new Binding(nameof(GlobalSearchResult.Detail)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _searchGrid.MouseDoubleClick += (_, _) => NavigateToSelectedSearchResult();
        Grid.SetRow(_searchGrid, 2); root.Children.Add(_searchGrid);
        tab.Content = root;
        return tab;
    }

    private TabItem BuildCommandAnalysisSubTab()
    {
        var tab = new TabItem { Header = "Befehlsanalyse" };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader("Cisco-Befehl erklären", "Ordnet eine Befehlszeile dem Register zu und erklärt Befehlswörter sowie Parameterpositionen."));
        var inputRow = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        var analyze = new Button { Content = "Analysieren", Style = TryFindResource("PrimaryButtonStyle") as Style };
        DockPanel.SetDock(analyze, Dock.Right);
        _commandInputBox = new TextBox { Margin = new Thickness(0, 0, 8, 0), Text = "ip ospf 10 area 0" };
        _commandInputBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) RefreshCommandAnalysis(); };
        analyze.Click += (_, _) => RefreshCommandAnalysis();
        inputRow.Children.Add(analyze); inputRow.Children.Add(_commandInputBox);
        Grid.SetRow(inputRow, 1); root.Children.Add(inputRow);
        _commandAnalysisBox = CreateCodeBox("Analyseergebnis");
        Grid.SetRow(_commandAnalysisBox, 2); root.Children.Add(_commandAnalysisBox);
        tab.Content = root;
        return tab;
    }

    private void BuildOperationsTab()
    {
        var tab = new TabItem { Header = "⇅  Betrieb" };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader("SSH-Übertragung und Backups", "Überträgt Konfigurationen per OpenSSH-Schlüssel oder Plink-Passwort und verwaltet Running-/Startup-Backups."));

        var settingsCard = CreateAdvancedCard();
        settingsCard.Margin = new Thickness(0, 10, 0, 10);
        var settingsGrid = new Grid();
        for (var i = 0; i < 6; i++) settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 5 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        settingsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        settingsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _sshHostBox = AddAdvancedInlineField(settingsGrid, 0, 0, "Host", "192.168.1.1", 145);
        _sshPortBox = AddAdvancedInlineField(settingsGrid, 0, 1, "Port", "22", 70);
        _sshUserBox = AddAdvancedInlineField(settingsGrid, 0, 2, "Benutzer", "netadmin", 130);
        _sshAuthModeCombo = new ComboBox { ItemsSource = new[] { "OpenSSH + Schlüssel", "Plink + Passwort" }, SelectedIndex = 0, Width = 170, Margin = new Thickness(6) };
        AddAdvancedInlineControl(settingsGrid, 0, 3, "Authentifizierung", _sshAuthModeCombo);
        _sshDelayBox = AddAdvancedInlineField(settingsGrid, 0, 4, "Zeilen-Delay ms", "45", 90);
        _sshSaveCheck = new CheckBox { Content = "Nach Übertragung speichern", IsChecked = true, Margin = new Thickness(10, 28, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        Grid.SetRow(_sshSaveCheck, 0); Grid.SetColumn(_sshSaveCheck, 5); settingsGrid.Children.Add(_sshSaveCheck);

        _sshKeyBox = AddAdvancedInlineField(settingsGrid, 1, 0, "Private Key", "", 300, 2);
        var browseKey = new Button { Content = "Key wählen", Margin = new Thickness(6, 27, 6, 6) };
        browseKey.Click += (_, _) => BrowseSshKey();
        Grid.SetRow(browseKey, 1); Grid.SetColumn(browseKey, 2); settingsGrid.Children.Add(browseKey);
        _sshPasswordBox = new PasswordBox { Width = 180, Margin = new Thickness(6), Padding = new Thickness(10, 7, 10, 7), Background = new SolidColorBrush(Color.FromRgb(14, 18, 25)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(39, 46, 58)) };
        AddAdvancedInlineControl(settingsGrid, 1, 3, "Plink-Passwort", _sshPasswordBox);
        var securityNote = new TextBlock { Text = "Passwörter werden weder im Projekt noch im Autosave gespeichert.", Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 29, 0, 0) };
        Grid.SetRow(securityNote, 1); Grid.SetColumn(securityNote, 4); Grid.SetColumnSpan(securityNote, 2); settingsGrid.Children.Add(securityNote);
        settingsCard.Child = settingsGrid;
        Grid.SetRow(settingsCard, 1); root.Children.Add(settingsCard);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(content, 2); root.Children.Add(content);

        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var test = new Button { Content = "Verbindung testen" };
        var send = new Button { Content = "Konfiguration senden", Style = TryFindResource("PrimaryButtonStyle") as Style };
        var runBackup = new Button { Content = "Running sichern" };
        var startBackup = new Button { Content = "Startup sichern" };
        test.Click += async (_, _) => await TestSshConnectionAsync();
        send.Click += async (_, _) => await SendCurrentConfigBySshAsync();
        runBackup.Click += async (_, _) => await CreateSshBackupAsync("Running-Config");
        startBackup.Click += async (_, _) => await CreateSshBackupAsync("Startup-Config");
        actions.Children.Add(test); actions.Children.Add(send); actions.Children.Add(runBackup); actions.Children.Add(startBackup);
        left.Children.Add(actions);
        _operationsOutputBox = CreateCodeBox("SSH-Ausgabe");
        Grid.SetRow(_operationsOutputBox, 1); left.Children.Add(_operationsOutputBox);
        content.Children.Add(left);

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var backupActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var exportBackup = new Button { Content = "Backup exportieren" };
        var compareBackup = new Button { Content = "Backup als Alt vergleichen" };
        var deleteBackup = new Button { Content = "Backup entfernen" };
        exportBackup.Click += (_, _) => ExportSelectedBackup();
        compareBackup.Click += (_, _) => UseSelectedBackupForDiff();
        deleteBackup.Click += (_, _) => DeleteSelectedBackup();
        backupActions.Children.Add(exportBackup); backupActions.Children.Add(compareBackup); backupActions.Children.Add(deleteBackup);
        right.Children.Add(backupActions);
        _backupGrid = new DataGrid { ItemsSource = _currentProject.Backups, IsReadOnly = true, AutoGenerateColumns = false };
        _backupGrid.Columns.Add(new DataGridTextColumn { Header = "Gerät", Binding = new Binding(nameof(BackupRecord.DeviceName)), Width = 135 });
        _backupGrid.Columns.Add(new DataGridTextColumn { Header = "Typ", Binding = new Binding(nameof(BackupRecord.BackupType)), Width = 120 });
        _backupGrid.Columns.Add(new DataGridTextColumn { Header = "Zeit", Binding = new Binding(nameof(BackupRecord.DisplayCreated)), Width = 145 });
        _backupGrid.Columns.Add(new DataGridTextColumn { Header = "Quelle", Binding = new Binding(nameof(BackupRecord.Source)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.SetRow(_backupGrid, 1); right.Children.Add(_backupGrid);
        Grid.SetColumn(right, 2); content.Children.Add(right);

        tab.Content = root;
        _tabsByName["Betrieb"] = tab;
        MainTabs.Items.Add(tab);
    }

    private void BuildDiagramAndReportTab()
    {
        var tab = new TabItem { Header = "◇  Diagramm / Bericht" };
        var inner = new TabControl();
        inner.Items.Add(BuildDiagramSubTab());
        inner.Items.Add(BuildReportSubTab());
        tab.Content = inner;
        _tabsByName["Diagramm / Bericht"] = tab;
        MainTabs.Items.Add(tab);
    }

    private TabItem BuildDiagramSubTab()
    {
        var tab = new TabItem { Header = "Netzwerkdiagramm" };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader("Topologie aus dem Projekt", "Stellt Projektgeräte und definierte Verbindungen grafisch dar und exportiert eine skalierbare SVG-Datei."));

        var linkRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        _linkSourceCombo = new ComboBox { Width = 155, DisplayMemberPath = nameof(ProjectDeviceSnapshot.Name), ItemsSource = _currentProject.Devices };
        _linkSourceIfBox = new TextBox { Width = 145, Text = "Gi0/0" };
        _linkTargetCombo = new ComboBox { Width = 155, DisplayMemberPath = nameof(ProjectDeviceSnapshot.Name), ItemsSource = _currentProject.Devices };
        _linkTargetIfBox = new TextBox { Width = 145, Text = "Gi0/0" };
        _linkTypeCombo = new ComboBox { Width = 120, ItemsSource = new[] { "Ethernet", "Trunk", "Port-Channel", "WAN", "Tunnel" }, SelectedIndex = 0 };
        var addLink = new Button { Content = "Verbindung hinzufügen", Style = TryFindResource("PrimaryButtonStyle") as Style };
        var removeLink = new Button { Content = "Letzte entfernen" };
        var refresh = new Button { Content = "Diagramm aktualisieren" };
        var export = new Button { Content = "SVG Export" };
        addLink.Click += (_, _) => AddProjectLink();
        removeLink.Click += (_, _) => RemoveLastProjectLink();
        refresh.Click += (_, _) => RefreshNetworkDiagram();
        export.Click += (_, _) => ExportNetworkDiagramSvg();
        foreach (var element in new UIElement[]
                 {
                     AdvancedInlineLabel("Quelle"), _linkSourceCombo, _linkSourceIfBox,
                     AdvancedInlineLabel("Ziel"), _linkTargetCombo, _linkTargetIfBox,
                     _linkTypeCombo, addLink, removeLink, refresh, export
                 })
        {
            if (element is FrameworkElement fe) fe.Margin = new Thickness(3);
            linkRow.Children.Add(element);
        }
        Grid.SetRow(linkRow, 1); root.Children.Add(linkRow);

        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(11, 14, 19)) };
        _diagramCanvas = new Canvas { Width = 1400, Height = 900, Background = new SolidColorBrush(Color.FromRgb(11, 14, 19)) };
        scroll.Content = _diagramCanvas;
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        tab.Content = root;
        return tab;
    }

    private TabItem BuildReportSubTab()
    {
        var tab = new TabItem { Header = "Berichtsexport" };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader("Projektbericht", "Erzeugt Geräteübersicht, IP-Plan, Verbindungen, Prüfungen und Testplan als HTML, DOCX oder PDF."));
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        var refresh = new Button { Content = "Vorschau aktualisieren", Style = TryFindResource("PrimaryButtonStyle") as Style };
        var html = new Button { Content = "HTML Export" };
        var docx = new Button { Content = "DOCX Export" };
        var pdf = new Button { Content = "PDF Export" };
        refresh.Click += async (_, _) => await RefreshReportPreviewAsync();
        html.Click += async (_, _) => await ExportProjectReportAsync("html");
        docx.Click += async (_, _) => await ExportProjectReportAsync("docx");
        pdf.Click += async (_, _) => await ExportProjectReportAsync("pdf");
        actions.Children.Add(refresh); actions.Children.Add(html); actions.Children.Add(docx); actions.Children.Add(pdf);
        Grid.SetRow(actions, 1); root.Children.Add(actions);
        _reportPreviewBox = CreateCodeBox("Berichtsvorschau");
        Grid.SetRow(_reportPreviewBox, 2); root.Children.Add(_reportPreviewBox);
        tab.Content = root;
        return tab;
    }

    private Border CreateAdvancedHeader(string title, string subtitle)
    {
        var border = CreateAdvancedCard();
        border.Padding = new Thickness(14, 11, 12, 11);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(232, 121, 26)), CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 12, 0) });
        var stack = new StackPanel { Margin = new Thickness(12, 0, 8, 0) };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold });
        stack.Children.Add(new TextBlock { Text = subtitle, Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        Grid.SetColumn(stack, 1); grid.Children.Add(stack);
        border.Child = grid;
        return border;
    }

    private static void AddAdvancedHeaderActions(Border header, UIElement actions)
    {
        if (header.Child is not Grid grid) return;
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
    }

    private static Border CreateAdvancedCard() => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(42, 49, 61)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14)
    };

    private static TextBlock AdvancedTitle(string text) => new() { Text = text, FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) };
    private static TextBlock AdvancedNote(string text) => new() { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private static TextBlock AdvancedInlineLabel(string text) => new() { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)), VerticalAlignment = VerticalAlignment.Center };

    private static TextBox AddAdvancedTextField(Panel parent, string label, string value, bool multiline = false, double height = 0)
    {
        parent.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 7, 0, 4) });
        var box = new TextBox { Text = value };
        if (multiline)
        {
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            box.Height = height > 0 ? height : 100;
            box.VerticalContentAlignment = VerticalAlignment.Top;
        }
        parent.Children.Add(box);
        return box;
    }

    private static TextBox AddAdvancedInlineField(Grid grid, int row, int column, string label, string value, double width, int columnSpan = 1)
    {
        var panel = new StackPanel { Margin = new Thickness(6) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)), Margin = new Thickness(0, 0, 0, 4) });
        var box = new TextBox { Text = value, Width = width, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(box);
        Grid.SetRow(panel, row); Grid.SetColumn(panel, column); Grid.SetColumnSpan(panel, columnSpan); grid.Children.Add(panel);
        return box;
    }

    private static void AddAdvancedInlineControl(Grid grid, int row, int column, string label, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(6) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)), Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(control);
        Grid.SetRow(panel, row); Grid.SetColumn(panel, column); grid.Children.Add(panel);
    }

    private static TextBox CreateCodeBox(string initialText) => new()
    {
        Text = initialText,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12.5,
        AcceptsReturn = true,
        AcceptsTab = true,
        TextWrapping = TextWrapping.NoWrap,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalContentAlignment = VerticalAlignment.Top,
        MinHeight = 100
    };

    private static void AddPortPlanColumns(DataGrid grid)
    {
        grid.Columns.Add(new DataGridTextColumn { Header = "Interface", Binding = new Binding(nameof(PortPlanEntry.Interface)), Width = 145 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Beschreibung", Binding = new Binding(nameof(PortPlanEntry.Description)), Width = 180 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Modus", Binding = new Binding(nameof(PortPlanEntry.Mode)), Width = 85 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Access", Binding = new Binding(nameof(PortPlanEntry.AccessVlan)), Width = 65 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Voice", Binding = new Binding(nameof(PortPlanEntry.VoiceVlan)), Width = 65 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Allowed VLANs", Binding = new Binding(nameof(PortPlanEntry.AllowedVlans)), Width = 135 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Native", Binding = new Binding(nameof(PortPlanEntry.NativeVlan)), Width = 65 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Port-Channel", Binding = new Binding(nameof(PortPlanEntry.ChannelGroup)), Width = 115 });
        grid.Columns.Add(new DataGridTextColumn { Header = "IP-Adresse", Binding = new Binding(nameof(PortPlanEntry.IpAddress)), Width = 180 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding(nameof(PortPlanEntry.State)), Width = 80 });
        grid.Columns.Add(new DataGridTextColumn { Header = "STP-Schutz", Binding = new Binding(nameof(PortPlanEntry.StpProtection)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
    }

    private async Task CaptureCurrentDeviceAsync(bool silent)
    {
        var values = CollectValues();
        var name = values.TryGetValue("hostname", out var hostname) && !string.IsNullOrWhiteSpace(hostname)
            ? hostname.Trim()
            : $"DEVICE-{_currentProject.Devices.Count + 1}";
        var snapshot = new ProjectDeviceSnapshot
        {
            Name = name,
            DeviceType = DeviceTypeCombo.SelectedItem?.ToString() ?? "Router",
            ConfigMode = ConfigModeCombo.SelectedItem?.ToString() ?? "Ohne VRF",
            Values = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase),
            Modules = ModuleCatalog.All.ToDictionary(m => m.Name, m => _moduleChecks.TryGetValue(m.Name, out var cb) && cb.IsChecked == true, StringComparer.OrdinalIgnoreCase),
            GeneratedConfiguration = await GenerateConfigAsync(),
            LastUpdatedUtc = DateTime.UtcNow
        };
        _currentProject.Devices.Add(snapshot);
        _projectDeviceGrid?.ScrollIntoView(snapshot);
        RefreshProjectDeviceBindings();
        RefreshNetworkDiagram();
        ScheduleAutoSave();
        if (!silent) MessageBox.Show(this, $"{snapshot.Name} wurde in das Projekt übernommen.", "Projekt", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task UpdateSelectedProjectDeviceAsync()
    {
        if (_projectDeviceGrid?.SelectedItem is not ProjectDeviceSnapshot snapshot)
        {
            MessageBox.Show(this, "Bitte zuerst ein Projektgerät auswählen.", "Projekt", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        snapshot.Name = GetFieldValue("hostname") is { Length: > 0 } hostname ? hostname : snapshot.Name;
        snapshot.DeviceType = DeviceTypeCombo.SelectedItem?.ToString() ?? snapshot.DeviceType;
        snapshot.ConfigMode = ConfigModeCombo.SelectedItem?.ToString() ?? snapshot.ConfigMode;
        snapshot.Values = new Dictionary<string, string>(CollectValues(), StringComparer.OrdinalIgnoreCase);
        snapshot.Modules = ModuleCatalog.All.ToDictionary(m => m.Name, m => _moduleChecks.TryGetValue(m.Name, out var cb) && cb.IsChecked == true, StringComparer.OrdinalIgnoreCase);
        snapshot.GeneratedConfiguration = await GenerateConfigAsync();
        snapshot.LastUpdatedUtc = DateTime.UtcNow;
        _projectDeviceGrid?.Items.Refresh();
        RefreshNetworkDiagram();
        ScheduleAutoSave();
    }

    private void ApplySelectedProjectDevice()
    {
        if (_projectDeviceGrid?.SelectedItem is not ProjectDeviceSnapshot snapshot) return;
        ApplyTemplateData(new TemplateData { Values = snapshot.Values, Modules = snapshot.Modules });
        NavigateToTab("Übersicht");
    }

    private void DuplicateSelectedProjectDevice()
    {
        if (_projectDeviceGrid?.SelectedItem is not ProjectDeviceSnapshot source) return;
        var copy = new ProjectDeviceSnapshot
        {
            Name = source.Name + "-COPY",
            DeviceType = source.DeviceType,
            ConfigMode = source.ConfigMode,
            Values = new Dictionary<string, string>(source.Values, StringComparer.OrdinalIgnoreCase),
            Modules = new Dictionary<string, bool>(source.Modules, StringComparer.OrdinalIgnoreCase),
            GeneratedConfiguration = source.GeneratedConfiguration,
            LastUpdatedUtc = DateTime.UtcNow
        };
        copy.Values["hostname"] = copy.Name;
        _currentProject.Devices.Add(copy);
        RefreshProjectDeviceBindings();
        RefreshNetworkDiagram();
        ScheduleAutoSave();
    }

    private async Task CreatePeerProjectDeviceAsync()
    {
        var localName = string.IsNullOrWhiteSpace(GetFieldValue("hostname")) ? "DEVICE" : GetFieldValue("hostname");
        var peerName = localName + "-PEER";
        var values = new Dictionary<string, string>(CollectValues(), StringComparer.OrdinalIgnoreCase) { ["hostname"] = peerName };
        var modules = ModuleCatalog.All.ToDictionary(m => m.Name, m => _moduleChecks.TryGetValue(m.Name, out var cb) && cb.IsChecked == true, StringComparer.OrdinalIgnoreCase);
        var snapshot = new ProjectDeviceSnapshot
        {
            Name = peerName,
            DeviceType = DeviceTypeCombo.SelectedItem?.ToString() ?? "Router",
            ConfigMode = ConfigModeCombo.SelectedItem?.ToString() ?? "Ohne VRF",
            Values = values,
            Modules = modules,
            GeneratedConfiguration = PeerConfigurationGenerator.GenerateDraft(BuildRequest(), peerName),
            LastUpdatedUtc = DateTime.UtcNow
        };
        _currentProject.Devices.Add(snapshot);
        RefreshProjectDeviceBindings();
        RefreshNetworkDiagram();
        ScheduleAutoSave();
        await Task.CompletedTask;
        MessageBox.Show(this, "Der Gegenstellenentwurf wurde als Projektgerät angelegt. Platzhalter <...> müssen vor einer Übertragung geprüft werden.", "Gegenstelle", MessageBoxButton.OK, MessageBoxImage.Information);
    }


    private void ShowSelectedProjectDeviceConfiguration()
    {
        if (_projectDeviceGrid?.SelectedItem is not ProjectDeviceSnapshot snapshot)
        {
            MessageBox.Show(this, "Bitte zuerst ein Projektgerät auswählen.", "Projekt", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SetConfigurationPreviewText(string.IsNullOrWhiteSpace(snapshot.GeneratedConfiguration)
            ? "Für dieses Projektgerät ist noch keine Konfiguration gespeichert."
            : snapshot.GeneratedConfiguration);
        NavigateToTab("Ausgabe");
    }

    private void ExportSelectedProjectDeviceConfiguration()
    {
        if (_projectDeviceGrid?.SelectedItem is not ProjectDeviceSnapshot snapshot)
        {
            MessageBox.Show(this, "Bitte zuerst ein Projektgerät auswählen.", "Projekt", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(snapshot.GeneratedConfiguration))
        {
            MessageBox.Show(this, "Für dieses Projektgerät ist noch keine Konfiguration gespeichert.", "Projekt", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "Gerätekonfiguration exportieren",
            Filter = "Cisco-Konfiguration (*.cfg)|*.cfg|Textdatei (*.txt)|*.txt",
            FileName = SanitizeFileName(snapshot.Name) + ".cfg"
        };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, snapshot.GeneratedConfiguration, new UTF8Encoding(false));
    }

    private void DeleteSelectedProjectDevice()
    {
        if (_projectDeviceGrid?.SelectedItem is not ProjectDeviceSnapshot snapshot) return;
        _currentProject.Devices.Remove(snapshot);
        foreach (var link in _currentProject.Links.Where(x => x.SourceDeviceId == snapshot.Id || x.TargetDeviceId == snapshot.Id).ToList()) _currentProject.Links.Remove(link);
        RefreshProjectDeviceBindings();
        RefreshNetworkDiagram();
        ScheduleAutoSave();
    }

    private void NewNetworkProject()
    {
        if (MessageBox.Show(this, "Aktuelles Projekt verwerfen und ein neues Projekt beginnen?", "Neues Projekt", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _currentProject = new NetworkProject();
        _currentProjectPath = string.Empty;
        RebindProjectCollections();
        RefreshProjectEditors();
        RefreshNetworkDiagram();
        ScheduleAutoSave();
    }

    private void OpenNetworkProject()
    {
        var dialog = new OpenFileDialog { Title = "Netzwerkprojekt öffnen", Filter = "Cisco-Projekt (*.ciscoproject.json)|*.ciscoproject.json|JSON (*.json)|*.json|Alle Dateien (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _currentProject = ProjectService.Load(dialog.FileName);
            _currentProjectPath = dialog.FileName;
            RebindProjectCollections();
            RefreshProjectEditors();
            RefreshNetworkDiagram();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Projekt konnte nicht geöffnet werden", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void SaveNetworkProject(bool saveAs)
    {
        SyncProjectEditors();
        if (saveAs || string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            var dialog = new SaveFileDialog { Title = "Netzwerkprojekt speichern", Filter = "Cisco-Projekt (*.ciscoproject.json)|*.ciscoproject.json|JSON (*.json)|*.json", FileName = SanitizeFileName(_currentProject.Name) + ".ciscoproject.json" };
            if (dialog.ShowDialog(this) != true) return;
            _currentProjectPath = dialog.FileName;
        }
        try
        {
            ProjectService.Save(_currentProject, _currentProjectPath);
            MessageBox.Show(this, "Projekt wurde gespeichert.", "Projekt", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Speicherfehler", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void SyncProjectEditors()
    {
        if (_projectNameBox != null && !string.IsNullOrWhiteSpace(_projectNameBox.Text)) _currentProject.Name = _projectNameBox.Text.Trim();
        if (_projectDescriptionBox != null) _currentProject.Description = _projectDescriptionBox.Text;
        _currentProject.ModifiedUtc = DateTime.UtcNow;
    }

    private void RefreshProjectEditors()
    {
        if (_projectNameBox != null) _projectNameBox.Text = _currentProject.Name;
        if (_projectDescriptionBox != null) _projectDescriptionBox.Text = _currentProject.Description;
    }

    private void NormalizeProjectCollections()
    {
        _currentProject.Devices ??= new();
        _currentProject.IpamEntries ??= new();
        _currentProject.Links ??= new();
        _currentProject.Backups ??= new();
    }

    private void RebindProjectCollections()
    {
        if (_projectDeviceGrid != null) _projectDeviceGrid.ItemsSource = _currentProject.Devices;
        if (_ipamGrid != null) _ipamGrid.ItemsSource = _currentProject.IpamEntries;
        if (_backupGrid != null) _backupGrid.ItemsSource = _currentProject.Backups;
        RefreshProjectDeviceBindings();
    }

    private void RefreshProjectDeviceBindings()
    {
        _projectDeviceGrid?.Items.Refresh();
        if (_linkSourceCombo != null) { _linkSourceCombo.ItemsSource = null; _linkSourceCombo.ItemsSource = _currentProject.Devices; }
        if (_linkTargetCombo != null) { _linkTargetCombo.ItemsSource = null; _linkTargetCombo.ItemsSource = _currentProject.Devices; }
    }

    private void ApplyTemplateData(TemplateData data)
    {
        foreach (var (name, value) in data.Values)
        {
            if (name.Equals("deviceType", StringComparison.OrdinalIgnoreCase)) DeviceTypeCombo.SelectedItem = value;
            else if (name.Equals("configMode", StringComparison.OrdinalIgnoreCase)) ConfigModeCombo.SelectedItem = value;
            else if (name.Equals("writeMem", StringComparison.OrdinalIgnoreCase)) WriteMemCombo.SelectedItem = value;
            else SetAdvancedFieldValue(name, value);
        }
        foreach (var (name, active) in data.Modules)
            if (_moduleChecks.TryGetValue(name, out var check)) check.IsChecked = active;
        ApplyFilters();
        UpdateConditionalFieldVisibility();
        UpdateModuleStatuses();
        UpdateStatusBar();
        RefreshStpPreview();
        ScheduleAutoSave();
    }

    private void SetAdvancedFieldValue(string fieldName, string value)
    {
        if (!_valueControls.TryGetValue(fieldName, out var control)) return;
        if (control is TextBox tb) tb.Text = value;
        else if (control is ComboBox cb)
        {
            var match = cb.Items.Cast<object>().FirstOrDefault(x => string.Equals(x?.ToString(), value, StringComparison.OrdinalIgnoreCase));
            if (match != null) cb.SelectedItem = match;
        }
        else if (control is CheckBox chk) chk.IsChecked = value.Equals("Ja", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ImportCurrentConfigIntoIpamAsync()
    {
        var config = await GenerateConfigAsync();
        var device = string.IsNullOrWhiteSpace(GetFieldValue("hostname")) ? "Aktuelles Gerät" : GetFieldValue("hostname");
        AddIpamEntries(IpamService.ImportFromConfiguration(config, device));
    }

    private void ImportProjectConfigsIntoIpam()
    {
        foreach (var device in _currentProject.Devices)
            AddIpamEntries(IpamService.ImportFromConfiguration(device.GeneratedConfiguration, device.Name));
    }

    private void AddIpamEntries(IEnumerable<IpamEntry> entries)
    {
        var added = 0;
        foreach (var entry in entries)
        {
            var exists = _currentProject.IpamEntries.Any(x =>
                x.Network.Equals(entry.Network, StringComparison.OrdinalIgnoreCase) &&
                x.PrefixLength == entry.PrefixLength &&
                x.Device.Equals(entry.Device, StringComparison.OrdinalIgnoreCase) &&
                x.Interface.Equals(entry.Interface, StringComparison.OrdinalIgnoreCase));
            if (exists) continue;
            _currentProject.IpamEntries.Add(entry);
            added++;
        }
        ScheduleAutoSave();
        MessageBox.Show(this, $"{added} neue IPAM-Einträge wurden übernommen.", "IPAM", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowIpamValidation()
    {
        var findings = IpamService.Validate(_currentProject.IpamEntries);
        MessageBox.Show(this,
            findings.Count == 0 ? "Keine Überschneidungen oder ungültigen Adressen erkannt." : string.Join(Environment.NewLine, findings.Take(40)),
            "IPAM-Prüfung",
            MessageBoxButton.OK,
            findings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void ExportIpamCsv()
    {
        var dialog = new SaveFileDialog { Title = "IPAM exportieren", Filter = "CSV (*.csv)|*.csv", FileName = "ip_adressplan.csv" };
        if (dialog.ShowDialog(this) != true) return;
        var sb = new StringBuilder("Netz;Praefix;VLAN;Gateway;DHCP-Start;DHCP-Ende;Geraet;Interface;Beschreibung\r\n");
        foreach (var x in _currentProject.IpamEntries)
            sb.AppendLine(string.Join(";", new[] { x.Network, x.PrefixLength.ToString(), x.Vlan, x.Gateway, x.DhcpStart, x.DhcpEnd, x.Device, x.Interface, x.Description }.Select(Csv)));
        File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
    }

    private async Task RefreshPortPlanFromCurrentAsync()
    {
        if (_portPlanGrid != null) _portPlanGrid.ItemsSource = PortPlanService.Parse(await GenerateConfigAsync());
    }

    private void RefreshPortPlanFromSelectedDevice()
    {
        if (_projectDeviceGrid?.SelectedItem is ProjectDeviceSnapshot device && _portPlanGrid != null)
            _portPlanGrid.ItemsSource = PortPlanService.Parse(device.GeneratedConfiguration);
    }

    private void ExportPortPlanCsv()
    {
        if (_portPlanGrid?.ItemsSource is not IEnumerable<PortPlanEntry> rows) return;
        var dialog = new SaveFileDialog { Title = "Portplan exportieren", Filter = "CSV (*.csv)|*.csv", FileName = "portplan.csv" };
        if (dialog.ShowDialog(this) != true) return;
        var sb = new StringBuilder("Interface;Beschreibung;Modus;Access-VLAN;Voice-VLAN;Allowed-VLANs;Native-VLAN;Port-Channel;IP-Adresse;Status;STP-Schutz\r\n");
        foreach (var x in rows)
            sb.AppendLine(string.Join(";", new[] { x.Interface, x.Description, x.Mode, x.AccessVlan, x.VoiceVlan, x.AllowedVlans, x.NativeVlan, x.ChannelGroup, x.IpAddress, x.State, x.StpProtection }.Select(Csv)));
        File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
    }

    private void RefreshAdvancedDependencies()
    {
        _advancedDependencyFindings = DependencyValidationService.Analyze(BuildRequest());
        if (_dependencyGrid != null) _dependencyGrid.ItemsSource = _advancedDependencyFindings;
    }

    private void AutoFixAdvancedDependencies()
    {
        RefreshAdvancedDependencies();
        var fixedCount = 0;
        foreach (var finding in _advancedDependencyFindings.Where(x => !string.IsNullOrWhiteSpace(x.FixKey)))
            if (ApplyAdvancedFix(finding.FixKey)) fixedCount++;
        ApplyFilters();
        UpdateConditionalFieldVisibility();
        UpdateModuleStatuses();
        UpdateStatusBar();
        RefreshAdvancedDependencies();
        ScheduleAutoSave();
        MessageBox.Show(this, $"{fixedCount} Korrekturen wurden angewendet. Platzhalter und sicherheitsrelevante Werte müssen geprüft werden.", "Auto-Korrektur", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool ApplyAdvancedFix(string fixKey)
    {
        switch (fixKey)
        {
            case "enable-ip-routing":
                ActivateModule("routingBase"); SetAdvancedFieldValue("ipRouting", "Ja"); return true;
            case "set-default-domain":
                ActivateModule("basic"); SetAdvancedFieldValue("domain", "firma.local"); return true;
            case "create-local-user":
                ActivateModule("userRights");
                if (string.IsNullOrWhiteSpace(GetFieldValue("privUserList"))) SetAdvancedFieldValue("privUserList", "netadmin|15|CHANGE_ME_STRONG_SECRET");
                return true;
            case "set-vty-password": SetAdvancedFieldValue("vtyPassword", "CHANGE_ME_VTY_PASSWORD"); return true;
            case "enable-aaa": ActivateModule("aaa"); SetAdvancedFieldValue("aaaLocal", "Ja"); return true;
            case "set-ospf-router-id": SetAdvancedFieldValue("ospfRid", "1.1.1.1"); return true;
            case "set-mst-name": SetAdvancedFieldValue("stpMstName", string.IsNullOrWhiteSpace(_currentProject.Name) ? "MST-REGION" : SanitizeFileName(_currentProject.Name).ToUpperInvariant()); return true;
            case "add-missing-vlans": AddMissingVlansFromCurrentValues(); return true;
            default: return false;
        }
    }

    private void AddMissingVlansFromCurrentValues()
    {
        ActivateModule("vlans");
        var existing = (GetFieldValue("vlanList") ?? string.Empty).Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        var ids = new HashSet<int>(existing.Select(x => x.Split('|')[0]).Where(x => int.TryParse(x, out _)).Select(int.Parse));
        foreach (var row in (GetFieldValue("sviList") + "\n" + GetFieldValue("trunkUplinkList")).Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = row.Split('|');
            if (parts.Length == 0) continue;
            IEnumerable<int> candidates = int.TryParse(parts[0], out var sviVlan)
                ? new[] { sviVlan }
                : parts.Length > 1 ? DependencyValidationService.ExpandVlanExpression(parts[1]) : Array.Empty<int>();
            foreach (var vlan in candidates.Where(x => x is >= 1 and <= 4094))
                if (ids.Add(vlan)) existing.Add($"{vlan}|VLAN-{vlan}");
        }
        SetAdvancedFieldValue("vlanList", string.Join(Environment.NewLine, existing));
    }

    private void ActivateModule(string moduleName)
    {
        if (_moduleChecks.TryGetValue(moduleName, out var check)) check.IsChecked = true;
    }

    private void NavigateToSelectedDependency()
    {
        if (_dependencyGrid?.SelectedItem is not DependencyFinding finding) return;
        NavigateToModuleAndField(finding.NavigationModule, finding.NavigationField);
    }

    private async Task RefreshSecurityAuditAsync()
    {
        _advancedSecurityFindings = SecurityAuditService.Analyze(await GenerateConfigAsync());
        if (_securityGrid != null) _securityGrid.ItemsSource = _advancedSecurityFindings;
    }

    private void RefreshConfigDiff()
    {
        if (_diffOldBox == null || _diffNewBox == null) return;
        _advancedDiff = ConfigDiffService.Compare(_diffOldBox.Text, _diffNewBox.Text);
        if (_diffGrid != null) _diffGrid.ItemsSource = _advancedDiff;
        if (_rollbackBox != null) _rollbackBox.Text = ConfigDiffService.CreateRollback(_advancedDiff);
    }

    private static void LoadTextIntoBox(TextBox? target)
    {
        if (target == null) return;
        var dialog = new OpenFileDialog { Filter = "Konfiguration (*.txt;*.cfg;*.conf)|*.txt;*.cfg;*.conf|Alle Dateien (*.*)|*.*" };
        if (dialog.ShowDialog() == true) target.Text = File.ReadAllText(dialog.FileName, Encoding.UTF8);
    }

    private void RefreshGlobalSearch()
    {
        if (_searchGrid != null) _searchGrid.ItemsSource = GlobalSearchService.Search(_searchBox?.Text ?? string.Empty);
    }

    private void NavigateToSelectedSearchResult()
    {
        if (_searchGrid?.SelectedItem is not GlobalSearchResult result) return;
        if (!string.IsNullOrWhiteSpace(result.ModuleName)) NavigateToModuleAndField(result.ModuleName, result.FieldName);
        else if (result.Kind == "Befehl")
        {
            NavigateToTab("Befehlsregister");
            if (_commandInputBox != null) _commandInputBox.Text = result.Command;
        }
    }

    private void NavigateToModuleAndField(string moduleName, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(moduleName)) return;
        var module = ModuleCatalog.All.FirstOrDefault(x => x.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
        if (module == null) return;
        ActivateModule(module.Name);
        NavigateToTab(module.Tab);
        if (_moduleExpanders.TryGetValue(module.Name, out var expander)) expander.IsExpanded = true;
        if (!string.IsNullOrWhiteSpace(fieldName) && _fieldWrappers.TryGetValue(fieldName, out var wrapper))
        {
            wrapper.BringIntoView();
            if (_valueControls.TryGetValue(fieldName, out var control)) control.Focus();
        }
        else if (_moduleCards.TryGetValue(module.Name, out var card)) card.BringIntoView();
    }

    private void RefreshCommandAnalysis()
    {
        var result = CommandAnalysisService.Analyze(_commandInputBox?.Text ?? string.Empty);
        if (_commandAnalysisBox == null) return;
        var sb = new StringBuilder();
        sb.AppendLine($"Eingabe: {result.Input}");
        sb.AppendLine($"Treffer: {(string.IsNullOrWhiteSpace(result.MatchedPattern) ? "Kein Muster" : result.MatchedPattern)}");
        sb.AppendLine($"Modul: {result.Module}");
        sb.AppendLine($"Kontext: {result.Mode}");
        sb.AppendLine($"Übereinstimmung: {result.Confidence:P0}");
        sb.AppendLine();
        sb.AppendLine(result.Meaning);
        sb.AppendLine();
        sb.AppendLine("Befehlsteile:");
        foreach (var part in result.Parts) sb.AppendLine(part);
        _commandAnalysisBox.Text = sb.ToString();
    }

    private SshConnectionSettings GetSshSettings()
    {
        _ = int.TryParse(_sshPortBox?.Text, out var port);
        _ = int.TryParse(_sshDelayBox?.Text, out var delay);
        return new SshConnectionSettings(
            _sshHostBox?.Text.Trim() ?? string.Empty,
            port is > 0 and <= 65535 ? port : 22,
            _sshUserBox?.Text.Trim() ?? string.Empty,
            _sshAuthModeCombo?.SelectedItem?.ToString() ?? "OpenSSH + Schlüssel",
            _sshKeyBox?.Text.Trim() ?? string.Empty,
            _sshPasswordBox?.Password ?? string.Empty,
            Math.Clamp(delay, 0, 5000),
            _sshSaveCheck?.IsChecked == true);
    }

    private void BrowseSshKey()
    {
        var dialog = new OpenFileDialog { Title = "Private SSH-Schlüsseldatei wählen", Filter = "Schlüsseldateien (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true && _sshKeyBox != null) _sshKeyBox.Text = dialog.FileName;
    }

    private async Task TestSshConnectionAsync()
    {
        if (_operationsOutputBox != null) _operationsOutputBox.Text = "Verbindung wird geprüft ...";
        var result = await SshDeviceService.TestTcpAsync(GetSshSettings());
        if (_operationsOutputBox != null) _operationsOutputBox.Text = result.Success ? result.Output : result.Error;
    }

    private async Task SendCurrentConfigBySshAsync()
    {
        var settings = GetSshSettings();
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Username))
        {
            MessageBox.Show(this, "Host und Benutzer müssen angegeben werden.", "SSH", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show(this, $"Konfiguration wirklich an {settings.Host}:{settings.Port} übertragen?", "SSH-Übertragung", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (_operationsOutputBox != null) _operationsOutputBox.Text = "Konfiguration wird übertragen ...";
        var result = await SshDeviceService.SendConfigurationAsync(settings, await GenerateConfigAsync());
        if (_operationsOutputBox != null) _operationsOutputBox.Text = result.Output + (string.IsNullOrWhiteSpace(result.Error) ? "" : "\nFEHLER:\n" + result.Error);
        MessageBox.Show(this, result.Success ? "Übertragung abgeschlossen." : "Übertragung fehlgeschlagen. Ausgabe prüfen.", "SSH", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
    }

    private async Task CreateSshBackupAsync(string backupType)
    {
        var settings = GetSshSettings();
        if (_operationsOutputBox != null) _operationsOutputBox.Text = $"{backupType} wird gelesen ...";
        var result = await SshDeviceService.ReadConfigurationAsync(settings, backupType);
        if (_operationsOutputBox != null) _operationsOutputBox.Text = result.Output + (string.IsNullOrWhiteSpace(result.Error) ? "" : "\nFEHLER:\n" + result.Error);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output)) return;
        var deviceName = _currentProject.Devices.FirstOrDefault(x => x.Name.Equals(GetFieldValue("hostname"), StringComparison.OrdinalIgnoreCase))?.Name;
        if (string.IsNullOrWhiteSpace(deviceName)) deviceName = string.IsNullOrWhiteSpace(GetFieldValue("hostname")) ? settings.Host : GetFieldValue("hostname");
        var backup = new BackupRecord
        {
            DeviceName = deviceName,
            BackupType = backupType,
            Source = $"SSH {settings.Host}:{settings.Port}",
            Content = ExtractCiscoBackupContent(result.Output),
            CreatedUtc = DateTime.UtcNow
        };
        _currentProject.Backups.Add(backup);
        _backupGrid?.Items.Refresh();
        ScheduleAutoSave();
    }

    private static string ExtractCiscoBackupContent(string output)
    {
        var lines = (output ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        var start = lines.FindIndex(x => x.Contains("Current configuration", StringComparison.OrdinalIgnoreCase) || x.TrimStart().StartsWith("version ", StringComparison.OrdinalIgnoreCase));
        if (start < 0) start = 0;
        var cleaned = lines.Skip(start)
            .Where(x => !RegexLikePrompt(x))
            .ToList();
        return string.Join(Environment.NewLine, cleaned).Trim();
    }

    private static bool RegexLikePrompt(string line)
    {
        var trimmed = line.Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[A-Za-z0-9_.-]+[>#]$") ||
               trimmed.Equals("terminal length 0", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("show running-config", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("show startup-config", StringComparison.OrdinalIgnoreCase);
    }

    private void ExportSelectedBackup()
    {
        if (_backupGrid?.SelectedItem is not BackupRecord backup) return;
        var dialog = new SaveFileDialog { Title = "Backup exportieren", Filter = "Cisco-Konfiguration (*.cfg)|*.cfg|Textdatei (*.txt)|*.txt", FileName = $"{SanitizeFileName(backup.DeviceName)}_{backup.BackupType.Replace("-", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.cfg" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, backup.Content, new UTF8Encoding(false));
        backup.FilePath = dialog.FileName;
        _backupGrid.Items.Refresh();
        ScheduleAutoSave();
    }

    private void UseSelectedBackupForDiff()
    {
        if (_backupGrid?.SelectedItem is not BackupRecord backup || _diffOldBox == null) return;
        _diffOldBox.Text = backup.Content;
        NavigateToTab("Analyse");
    }

    private void DeleteSelectedBackup()
    {
        if (_backupGrid?.SelectedItem is BackupRecord backup) _currentProject.Backups.Remove(backup);
        ScheduleAutoSave();
    }

    private void AddProjectLink()
    {
        if (_linkSourceCombo?.SelectedItem is not ProjectDeviceSnapshot source || _linkTargetCombo?.SelectedItem is not ProjectDeviceSnapshot target)
        {
            MessageBox.Show(this, "Quelle und Ziel müssen ausgewählt werden.", "Verbindung", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (source.Id == target.Id)
        {
            MessageBox.Show(this, "Quelle und Ziel dürfen nicht identisch sein.", "Verbindung", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _currentProject.Links.Add(new ProjectLink
        {
            SourceDeviceId = source.Id,
            TargetDeviceId = target.Id,
            SourceInterface = _linkSourceIfBox?.Text.Trim() ?? string.Empty,
            TargetInterface = _linkTargetIfBox?.Text.Trim() ?? string.Empty,
            LinkType = _linkTypeCombo?.SelectedItem?.ToString() ?? "Ethernet"
        });
        RefreshNetworkDiagram();
        ScheduleAutoSave();
    }

    private void RemoveLastProjectLink()
    {
        if (_currentProject.Links.Count > 0) _currentProject.Links.RemoveAt(_currentProject.Links.Count - 1);
        RefreshNetworkDiagram();
        ScheduleAutoSave();
    }

    private void RefreshNetworkDiagram()
    {
        if (_diagramCanvas == null) return;
        _diagramCanvas.Children.Clear();
        var layout = NetworkDiagramService.CalculateLayout(_currentProject, _diagramCanvas.Width, _diagramCanvas.Height);
        foreach (var link in _currentProject.Links)
        {
            if (!layout.TryGetValue(link.SourceDeviceId, out var a) || !layout.TryGetValue(link.TargetDeviceId, out var b)) continue;
            var line = new Line
            {
                X1 = a.X + a.Width / 2, Y1 = a.Y + a.Height / 2,
                X2 = b.X + b.Width / 2, Y2 = b.Y + b.Height / 2,
                Stroke = new SolidColorBrush(Color.FromRgb(232, 121, 26)), StrokeThickness = 3
            };
            _diagramCanvas.Children.Add(line);
            var label = new TextBlock { Text = $"{link.SourceInterface} ↔ {link.TargetInterface}", Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)), Background = new SolidColorBrush(Color.FromArgb(210, 11, 14, 19)), Padding = new Thickness(4, 2, 4, 2) };
            Canvas.SetLeft(label, (line.X1 + line.X2) / 2 - 50); Canvas.SetTop(label, (line.Y1 + line.Y2) / 2 - 18); _diagramCanvas.Children.Add(label);
        }
        foreach (var device in _currentProject.Devices)
        {
            if (!layout.TryGetValue(device.Id, out var p)) continue;
            var border = new Border
            {
                Width = p.Width, Height = p.Height, CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(23, 28, 37)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(232, 121, 26)), BorderThickness = new Thickness(2),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = device.Name, FontSize = 16, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = device.DeviceType, Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) }
                    }
                },
                ToolTip = $"{device.Name}\n{device.DeviceType}\n{device.ConfigMode}"
            };
            Canvas.SetLeft(border, p.X); Canvas.SetTop(border, p.Y); _diagramCanvas.Children.Add(border);
        }
    }

    private void ExportNetworkDiagramSvg()
    {
        var dialog = new SaveFileDialog { Title = "Netzwerkdiagramm exportieren", Filter = "SVG (*.svg)|*.svg", FileName = "netzwerkdiagramm.svg" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, NetworkDiagramService.BuildSvg(_currentProject), new UTF8Encoding(false));
    }

    private async Task RefreshReportPreviewAsync()
    {
        await EnsureProjectContainsCurrentDeviceAsync();
        _advancedDependencyFindings = DependencyValidationService.Analyze(BuildRequest());
        _advancedSecurityFindings = SecurityAuditService.Analyze(await GenerateConfigAsync());
        if (_reportPreviewBox != null) _reportPreviewBox.Text = ReportExportService.BuildPlainText(_currentProject, _advancedDependencyFindings, _advancedSecurityFindings);
    }

    private async Task ExportProjectReportAsync(string format)
    {
        await RefreshReportPreviewAsync();
        var extension = format.ToLowerInvariant();
        var filter = extension switch
        {
            "html" => "HTML (*.html)|*.html",
            "docx" => "Word-Dokument (*.docx)|*.docx",
            _ => "PDF (*.pdf)|*.pdf"
        };
        var dialog = new SaveFileDialog { Title = "Projektbericht exportieren", Filter = filter, FileName = SanitizeFileName(_currentProject.Name) + "_Bericht." + extension };
        if (dialog.ShowDialog(this) != true) return;
        var plain = _reportPreviewBox?.Text ?? string.Empty;
        try
        {
            if (extension == "html") ReportExportService.ExportHtml(dialog.FileName, _currentProject, _advancedDependencyFindings, _advancedSecurityFindings);
            else if (extension == "docx") ReportExportService.ExportDocx(dialog.FileName, plain);
            else ReportExportService.ExportPdf(dialog.FileName, plain);
            MessageBox.Show(this, "Bericht wurde exportiert.", "Bericht", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Exportfehler", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task EnsureProjectContainsCurrentDeviceAsync()
    {
        var hostname = GetFieldValue("hostname");
        if (_currentProject.Devices.Count == 0 || (!string.IsNullOrWhiteSpace(hostname) && !_currentProject.Devices.Any(x => x.Name.Equals(hostname, StringComparison.OrdinalIgnoreCase))))
            await CaptureCurrentDeviceAsync(true);
    }

    private void InitializeAutoSave()
    {
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _autoSaveTimer.Tick += (_, _) => SaveAutoSaveState();
        Loaded += (_, _) =>
        {
            TryRestoreAutoSaveState();
            _autoSaveTimer?.Start();
        };
        Closing += (_, _) => SaveAutoSaveState();
    }

    private void ScheduleAutoSave()
    {
        if (_autoSaveTimer == null) return;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Interval = TimeSpan.FromSeconds(20);
        _autoSaveTimer.Start();
    }

    private void SaveAutoSaveState()
    {
        try
        {
            SyncProjectEditors();
            var state = new AutoSaveState
            {
                Project = _currentProject,
                CurrentDevice = new TemplateData
                {
                    Values = CollectValues(),
                    Modules = ModuleCatalog.All.ToDictionary(m => m.Name, m => _moduleChecks.TryGetValue(m.Name, out var cb) && cb.IsChecked == true, StringComparer.OrdinalIgnoreCase)
                },
                SavedUtc = DateTime.UtcNow
            };
            var path = ProjectService.AutoSavePath;
            Directory.CreateDirectory(IOPath.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            if (_autoSaveTimer != null) _autoSaveTimer.Interval = TimeSpan.FromSeconds(60);
        }
        catch
        {
            // Autosave darf die Bedienung nicht unterbrechen.
        }
    }

    private void TryRestoreAutoSaveState()
    {
        var path = ProjectService.AutoSavePath;
        if (!File.Exists(path)) return;
        try
        {
            var state = JsonSerializer.Deserialize<AutoSaveState>(File.ReadAllText(path, Encoding.UTF8), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (state == null || DateTime.UtcNow - state.SavedUtc > TimeSpan.FromDays(30)) return;
            if (MessageBox.Show(this, $"Es wurde ein Autosave vom {state.SavedUtc.ToLocalTime():dd.MM.yyyy HH:mm} gefunden. Wiederherstellen?", "Autosave", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _currentProject = state.Project ?? new NetworkProject();
            NormalizeProjectCollections();
            RebindProjectCollections();
            RefreshProjectEditors();
            ApplyTemplateData(state.CurrentDevice ?? new TemplateData());
            RefreshNetworkDiagram();
        }
        catch
        {
            // Beschädigte Autosaves werden ignoriert.
        }
    }

    private FrameworkElement CreateModuleLivePreview(ModuleDefinition module)
    {
        var box = new TextBox
        {
            Text = "Modul aktivieren oder Vorschau aktualisieren.",
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 110,
            MaxHeight = 260,
            VerticalContentAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromRgb(8, 11, 16))
        };
        _moduleLivePreviewBoxes[module.Name] = box;

        var refresh = new Button { Content = "Aktualisieren", Style = TryFindResource("SmallButtonStyle") as Style, HorizontalAlignment = HorizontalAlignment.Right };
        refresh.Click += async (_, _) => await RefreshModuleLivePreviewAsync(module.Name);
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(refresh);
        Grid.SetRow(box, 1);
        panel.Children.Add(box);
        return new Expander
        {
            Header = "Live-Befehlsvorschau dieses Moduls",
            IsExpanded = false,
            Margin = new Thickness(0, 4, 0, 0),
            Content = panel
        };
    }

    private void RefreshModuleLivePreviewForField(string fieldName)
    {
        var module = ModuleCatalog.All.FirstOrDefault(x => x.Fields.Any(f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase)));
        if (module != null) _ = RefreshModuleLivePreviewAsync(module.Name);
    }

    private void RefreshModuleLivePreview(string moduleName) => _ = RefreshModuleLivePreviewAsync(moduleName);

    private async Task RefreshModuleLivePreviewAsync(string moduleName)
    {
        if (!_moduleLivePreviewBoxes.TryGetValue(moduleName, out var box)) return;
        if (!_moduleChecks.TryGetValue(moduleName, out var check) || check.IsChecked != true)
        {
            box.Text = "Modul ist nicht aktiv.";
            return;
        }
        try
        {
            var config = await NativeCiscoGenerator.GenerateAsync(BuildRequest());
            var title = GetGeneratorSectionTitle(moduleName);
            var section = ExtractGeneratedSection(config, title);
            if (section.StartsWith("Für die aktuellen Angaben", StringComparison.OrdinalIgnoreCase))
                section = ExtractLikelyModuleLines(config, ModuleCatalog.All.First(x => x.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase)));
            box.Text = section;
            box.ScrollToHome();
        }
        catch (Exception ex) { box.Text = "Vorschaufehler: " + ex.Message; }
    }

    private static string GetGeneratorSectionTitle(string moduleName) => moduleName switch
    {
        "basic" => "BASIS",
        "routingBase" => "ROUTING ALLGEMEIN",
        "banner" => "BANNER",
        "ssh" => "SSH / LINE VTY",
        "aaa" => "AAA / CONSOLE",
        "radiusTacacs" => "RADIUS TACACS AAA SERVER",
        "monitoringBase" => "MONITORING",
        "interfaces" => "INTERFACES",
        "ranges" => "INTERFACE RANGES",
        "routerStick" => "ROUTER ON A STICK",
        "subinterfaces" => "SUBINTERFACES",
        "vlans" => "VLANS UND SVIS",
        "switchSec" => "SWITCH SECURITY",
        "staticRoutes" => "STATISCHE ROUTEN",
        "ospf" => "OSPF",
        "isis" => "ISIS",
        "bgp" => "BGP",
        "fhrp" => "FHRP",
        "ipv6" => "IPV6",
        "dhcp" => "DHCP",
        "acl" => "ACL",
        "security" => "HARDENING",
        "nat" => "NAT",
        "vpn" => "GRE",
        "customCommands" => "ZUSATZBEFEHLE",
        "accessBaseline" => "ACCESS PORT BASELINE",
        "greIpsec" => "GRE IPSEC",
        "vrfLite" => "VRF LITE",
        "qosBasic" => "QOS BASIS",
        "errdisableRecovery" => "ERRDISABLE RECOVERY",
        "interfaceProfiles" => "INTERFACE PROFILE",
        "trunkUplink" => "TRUNK UPLINK",
        "etherChannelExt" => "ETHERCHANNEL",
        "stpExtended" => "STP ERWEITERT",
        "switchSecExt" => "SWITCH SECURITY ERWEITERT",
        "portSecurityExt" => "PORT SECURITY ERWEITERT",
        "routeMapFilter" => "ROUTE MAP FILTER",
        "ipSlaTracking" => "IP SLA TRACKING",
        "ipv6Extended" => "IPV6 ERWEITERT",
        "ospfv3" => "OSPFV3",
        "wanFailover" => "WAN FAILOVER",
        "zoneFirewall" => "ZONE BASED FIREWALL",
        "dmzAssistant" => "DMZ ASSISTENT",
        "vpnAdvanced" => "VPN ERWEITERT",
        "userRights" => "BENUTZER RECHTE",
        "vrfDefs" => "VRF DEFINITIONEN",
        "vrfSvi" => "VRF SVIS",
        "vrfStaticRoutes" => "VRF STATISCHE ROUTEN",
        "vrfOspf" => "VRF OSPF",
        "vrfOspfv3" => "VRF OSPFV3",
        "vrfBgp" => "VRF BGP",
        "ipv6RoutingProtocols" => "IPV6 ROUTING PROTOKOLLE",
        "interfaceRoles" => "INTERFACE ROLLEN",
        "vlanIpPlan" => "VLAN IP PLAN",
        "aclAssistant" => "ACL ASSISTENT",
        "ospfAdvanced" => "OSPF ERWEITERT",
        "bgpAdvanced" => "BGP ERWEITERT",
        "mpls" => "MPLS",
        "voip" => "VOIP",
        "qinq" => "QINQ",
        _ => moduleName.ToUpperInvariant()
    };

    private static string ExtractLikelyModuleLines(string config, ModuleDefinition module)
    {
        var terms = module.Fields.SelectMany(f => (f.Label + " " + f.Name).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(x => x.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
        var lines = (config ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Where(line => terms.Any(term => line.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Take(80)
            .ToList();
        return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "Für dieses Modul wurden mit den aktuellen Werten keine separaten Befehle erzeugt.";
    }

    private static string Csv(string value)
    {
        value ??= string.Empty;
        return value.Contains(';') || value.Contains('"') || value.Contains('\n') ? '"' + value.Replace("\"", "\"\"") + '"' : value;
    }

    private static string SanitizeFileName(string value)
    {
        var result = value ?? "Projekt";
        foreach (var invalid in IOPath.GetInvalidFileNameChars()) result = result.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(result) ? "Projekt" : result.Trim();
    }
}
