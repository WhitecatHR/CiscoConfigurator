using Microsoft.Win32;
using System.IO;
using System.Globalization;
using IOPath = System.IO.Path;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private TextBox? _projectNumberBox;
    private TextBox? _projectCustomerBox;
    private TextBox? _projectLocationBox;
    private TextBox? _projectManagerBox;
    private TextBox? _projectAuthorBox;
    private TextBox? _projectVersionBox;
    private TextBox? _projectStatusBox;
    private DataGrid? _projectDeviceGrid;
    private DataGrid? _ipamGrid;
    private DataGrid? _portPlanGrid;
    private DataGrid? _dependencyGrid;
    private DataGrid? _securityGrid;
    private DataGrid? _diffGrid;
    private DataGrid? _searchGrid;
    private DataGrid? _backupGrid;
    private DataGrid? _aclGrid;
    private DataGrid? _aclFindingsGrid;
    private DataGrid? _aclBindingsGrid;
    private TextBox? _aclPreviewBox;
    private CheckBox? _routingOverlayCheck;
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
    private TextBox? _linkDescriptionBox;
    private TextBox? _reportPreviewBox;
    private readonly Dictionary<string, Border> _diagramDeviceElements = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DiagramLinkVisual> _diagramLinkElements = new();
    private ProjectDeviceSnapshot? _draggedDiagramDevice;
    private Border? _draggedDiagramElement;
    private Point _diagramDragOffset;

    private sealed class DiagramLinkVisual
    {
        public required ProjectLink Link { get; init; }
        public required Line Line { get; init; }
        public required TextBlock Label { get; init; }
        public required Border LabelBorder { get; init; }
        public required Ellipse SourceEndpoint { get; init; }
        public required Ellipse TargetEndpoint { get; init; }
    }
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
        var tab = new TabItem { Header = LocalizationService.Get("text.projekt") };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = CreateAdvancedHeader(
            "Mehrgeräte-Projekt",
            "Verwaltet mehrere Router und Switches mit gemeinsamen IPAM-, Link-, Backup- und Berichtsdaten.");
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var newButton = new Button { Content = LocalizationService.Get("text.neu") };
        var openButton = new Button { Content = LocalizationService.Get("text.offnen") };
        var saveButton = new Button { Content = LocalizationService.Get("text.speichern"), Style = TryFindResource("PrimaryButtonStyle") as Style };
        var packageButton = new Button { Content = "Projektpaket ZIP" };
        newButton.Click += (_, _) => NewNetworkProject();
        openButton.Click += (_, _) => OpenNetworkProject();
        saveButton.Click += (_, _) => SaveNetworkProject(false);
        packageButton.Click += async (_, _) => await ExportProjectPackageAsync();
        headerActions.Children.Add(newButton);
        headerActions.Children.Add(openButton);
        headerActions.Children.Add(saveButton);
        headerActions.Children.Add(packageButton);
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
        _projectDescriptionBox = AddAdvancedTextField(details, "Beschreibung", _currentProject.Description, true, 100);
        _currentProject.ProjectInfo ??= new ProjectPlanInfo();
        _projectNumberBox = AddAdvancedTextField(details, "Projektnummer", _currentProject.ProjectInfo.ProjectNumber);
        _projectCustomerBox = AddAdvancedTextField(details, "Organisation / Kunde", _currentProject.ProjectInfo.Customer);
        _projectLocationBox = AddAdvancedTextField(details, "Standort", _currentProject.ProjectInfo.Location);
        _projectManagerBox = AddAdvancedTextField(details, "Projektleiter", _currentProject.ProjectInfo.ProjectManager);
        _projectAuthorBox = AddAdvancedTextField(details, "Bearbeiter", _currentProject.ProjectInfo.Author);
        _projectVersionBox = AddAdvancedTextField(details, "Version", _currentProject.ProjectInfo.Version);
        _projectStatusBox = AddAdvancedTextField(details, "Status", _currentProject.ProjectInfo.Status);
        _projectNameBox.TextChanged += (_, _) => ScheduleAutoSave();
        _projectDescriptionBox.TextChanged += (_, _) => ScheduleAutoSave();
        _projectNumberBox.TextChanged += (_, _) => ScheduleAutoSave();
        _projectCustomerBox.TextChanged += (_, _) => ScheduleAutoSave();
        _projectLocationBox.TextChanged += (_, _) => ScheduleAutoSave();
        _projectManagerBox.TextChanged += (_, _) => ScheduleAutoSave();
        _projectAuthorBox.TextChanged += (_, _) => ScheduleAutoSave();
        _projectVersionBox.TextChanged += (_, _) => ScheduleAutoSave();
        _projectStatusBox.TextChanged += (_, _) => ScheduleAutoSave();
        details.Children.Add(AdvancedNote("Beim Speichern werden Geräte, IPAM, Verbindungen, Backups und Netzplan-Metadaten gemeinsam in einer .ciscoproject.json-Datei abgelegt."));
        detailsCard.Child = details;
        content.Children.Add(detailsCard);

        var deviceArea = new Grid();
        deviceArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        deviceArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(deviceArea, 2);
        content.Children.Add(deviceArea);

        var deviceButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var captureButton = new Button { Content = LocalizationService.Get("text.aktuelles_gerat_ubernehmen"), Style = TryFindResource("PrimaryButtonStyle") as Style };
        var updateButton = new Button { Content = LocalizationService.Get("text.ausgewahltes_aktualisieren") };
        var applyButton = new Button { Content = LocalizationService.Get("text.ausgewahltes_laden") };
        var duplicateButton = new Button { Content = LocalizationService.Get("text.duplizieren") };
        var peerButton = new Button { Content = LocalizationService.Get("text.gegenstelle_erzeugen") };
        var showConfigButton = new Button { Content = LocalizationService.Get("text.konfiguration_anzeigen") };
        var exportConfigButton = new Button { Content = LocalizationService.Get("text.konfiguration_exportieren") };
        var deleteButton = new Button { Content = LocalizationService.Get("text.entfernen") };
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
            IsReadOnly = false,
            SelectionMode = DataGridSelectionMode.Single,
            AutoGenerateColumns = false
        };
        _projectDeviceGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("header.device"), Binding = new Binding(nameof(ProjectDeviceSnapshot.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
        _projectDeviceGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.typ"), Binding = new Binding(nameof(ProjectDeviceSnapshot.DeviceType)), Width = 115, IsReadOnly = true });
        _projectDeviceGrid.Columns.Add(new DataGridTextColumn { Header = "Standort", Binding = new Binding(nameof(ProjectDeviceSnapshot.Site)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 105 });
        _projectDeviceGrid.Columns.Add(new DataGridComboBoxColumn { Header = "Topologierolle", SelectedItemBinding = new Binding(nameof(ProjectDeviceSnapshot.TopologyRole)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, ItemsSource = new[] { "Automatisch", "WAN", "Core", "Distribution", "Access", "Other" }, Width = 125 });
        _projectDeviceGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("header.mode"), Binding = new Binding(nameof(ProjectDeviceSnapshot.ConfigMode)), Width = 110, IsReadOnly = true });
        _projectDeviceGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.status"), Binding = new Binding(nameof(ProjectDeviceSnapshot.Status)), Width = 155, IsReadOnly = true });
        _projectDeviceGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.geandert"), Binding = new Binding(nameof(ProjectDeviceSnapshot.LastUpdatedUtc)) { StringFormat = "dd.MM.yyyy HH:mm" }, Width = 140, IsReadOnly = true });
        _projectDeviceGrid.CellEditEnding += (_, _) => ScheduleAutoSave();
        _projectDeviceGrid.MouseDoubleClick += (_, _) => ApplySelectedProjectDevice();
        Grid.SetRow(_projectDeviceGrid, 1);
        deviceArea.Children.Add(_projectDeviceGrid);

        tab.Content = root;
        _tabsByName["Projekt"] = tab;
        MainTabs.Items.Add(tab);
    }

    private void BuildIpamAndPortPlanTab()
    {
        var tab = new TabItem { Header = "IPAM" };
        var inner = new TabControl();
        inner.Items.Add(BuildIpamSubTab());
        inner.Items.Add(BuildPortPlanSubTab());
        tab.Content = inner;
        _tabsByName["IPAM / Ports"] = tab;
        MainTabs.Items.Add(tab);
    }

    private TabItem BuildIpamSubTab()
    {
        var tab = new TabItem { Header = LocalizationService.Get("text.ip_adressverwaltung") };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        root.Children.Add(CreateAdvancedHeader("IPAM", "Zentrale IPv4-/IPv6-Netzplanung mit VLAN, Gateway, DHCP-Bereich, Gerät und Interface."));
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        var add = new Button { Content = LocalizationService.Get("text.eintrag_hinzufugen"), Style = TryFindResource("PrimaryButtonStyle") as Style };
        var remove = new Button { Content = LocalizationService.Get("text.ausgewahlten_entfernen") };
        var importCurrent = new Button { Content = LocalizationService.Get("text.aus_aktueller_konfiguration") };
        var importProject = new Button { Content = LocalizationService.Get("text.aus_allen_projektgeraten") };
        var validate = new Button { Content = LocalizationService.Get("text.uberschneidungen_prufen") };
        var export = new Button { Content = LocalizationService.Get("common.csv_export") };
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
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("ipam.network_address"), Binding = new Binding(nameof(IpamEntry.Network)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 145 });
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.prafix"), Binding = new Binding(nameof(IpamEntry.PrefixLength)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 65 });
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.vlan"), Binding = new Binding(nameof(IpamEntry.Vlan)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 65 });
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("ipam.gateway"), Binding = new Binding(nameof(IpamEntry.Gateway)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 145 });
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.dhcp_start"), Binding = new Binding(nameof(IpamEntry.DhcpStart)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 145 });
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.dhcp_ende"), Binding = new Binding(nameof(IpamEntry.DhcpEnd)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 145 });
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("header.device"), Binding = new Binding(nameof(IpamEntry.Device)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 130 });
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.interface"), Binding = new Binding(nameof(IpamEntry.Interface)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 150 });
        _ipamGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.beschreibung"), Binding = new Binding(nameof(IpamEntry.Description)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _ipamGrid.CellEditEnding += (_, _) => ScheduleAutoSave();
        Grid.SetRow(_ipamGrid, 2);
        root.Children.Add(_ipamGrid);
        tab.Content = root;
        return tab;
    }

    private TabItem BuildPortPlanSubTab()
    {
        var tab = new TabItem { Header = LocalizationService.Get("ipam.port_plan") };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader("Interface- und Portplan", "Erzeugt aus der aktuellen oder gespeicherten Konfiguration eine tabellarische Portübersicht."));
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        var current = new Button { Content = LocalizationService.Get("text.aktuelle_konfiguration"), Style = TryFindResource("PrimaryButtonStyle") as Style };
        var selected = new Button { Content = LocalizationService.Get("text.ausgewahltes_projektgerat") };
        var export = new Button { Content = LocalizationService.Get("common.csv_export") };
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
        var tab = new TabItem { Header = LocalizationService.Get("tab.analysis") };
        var inner = new TabControl();
        inner.Items.Add(BuildDependencySubTab());
        inner.Items.Add(BuildSecuritySubTab());
        inner.Items.Add(BuildDiffSubTab());
        inner.Items.Add(BuildGlobalSearchSubTab());
        inner.Items.Add(BuildAclWorkspaceSubTab());
        inner.Items.Add(BuildCommandAnalysisSubTab());
        tab.Content = inner;
        _tabsByName["Analyse"] = tab;
        MainTabs.Items.Add(tab);
    }

    private TabItem BuildDependencySubTab()
    {
        var tab = new TabItem { Header = LocalizationService.Get("text.abhangigkeiten") };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader("Abhängigkeits- und Konfliktprüfung", "Prüft Modulbeziehungen, fehlende Pflichtwerte, VLAN-/Trunk-Konsistenz und Routingvoraussetzungen."));
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        var refresh = new Button { Content = LocalizationService.Get("text.prufen"), Style = TryFindResource("PrimaryButtonStyle") as Style };
        var fix = new Button { Content = LocalizationService.Get("text.alle_automatisch_korrigierbaren_beheben") };
        var navigate = new Button { Content = LocalizationService.Get("text.zum_ausgewahlten_feld") };
        refresh.Click += (_, _) => RefreshAdvancedDependencies();
        fix.Click += (_, _) => AutoFixAdvancedDependencies();
        navigate.Click += (_, _) => NavigateToSelectedDependency();
        actions.Children.Add(refresh); actions.Children.Add(fix); actions.Children.Add(navigate);
        Grid.SetRow(actions, 1); root.Children.Add(actions);

        _dependencyGrid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false };
        _dependencyGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.stufe"), Binding = new Binding(nameof(DependencyFinding.Severity)), Width = 85 });
        _dependencyGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.bereich"), Binding = new Binding(nameof(DependencyFinding.Area)), Width = 115 });
        _dependencyGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.meldung"), Binding = new Binding(nameof(DependencyFinding.Message)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _dependencyGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.auto_fix"), Binding = new Binding(nameof(DependencyFinding.FixKey)), Width = 180 });
        _dependencyGrid.MouseDoubleClick += (_, _) => NavigateToSelectedDependency();
        Grid.SetRow(_dependencyGrid, 2); root.Children.Add(_dependencyGrid);
        tab.Content = root;
        return tab;
    }

    private TabItem BuildSecuritySubTab()
    {
        var tab = new TabItem { Header = LocalizationService.Get("analysis.security") };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader("Sicherheitsprüfung", "Analysiert die erzeugte Konfiguration auf typische Management-, Passwort-, SNMP-, STP- und VLAN-Risiken."));
        var refresh = new Button { Content = LocalizationService.Get("text.aktuelle_konfiguration_prufen"), Style = TryFindResource("PrimaryButtonStyle") as Style, Margin = new Thickness(0, 8, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        refresh.Click += async (_, _) => await RefreshSecurityAuditAsync();
        Grid.SetRow(refresh, 1); root.Children.Add(refresh);
        _securityGrid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false };
        _securityGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.stufe"), Binding = new Binding(nameof(SecurityFinding.Severity)), Width = 85 });
        _securityGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.kategorie"), Binding = new Binding(nameof(SecurityFinding.Category)), Width = 120 });
        _securityGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.feststellung"), Binding = new Binding(nameof(SecurityFinding.Message)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _securityGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.empfehlung.61d30d48"), Binding = new Binding(nameof(SecurityFinding.Recommendation)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.SetRow(_securityGrid, 2); root.Children.Add(_securityGrid);
        tab.Content = root;
        return tab;
    }

    private TabItem BuildDiffSubTab()
    {
        var tab = new TabItem { Header = LocalizationService.Get("text.vergleich_rollback") };
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
        var oldFile = new Button { Content = LocalizationService.Get("text.alt_aus_datei") };
        var newFile = new Button { Content = LocalizationService.Get("text.neu_aus_datei") };
        var currentAsNew = new Button { Content = LocalizationService.Get("text.aktuell_als_neu") };
        var selectedAsOld = new Button { Content = LocalizationService.Get("text.projektgerat_als_alt") };
        var compare = new Button { Content = LocalizationService.Get("common.compare"), Style = TryFindResource("PrimaryButtonStyle") as Style };
        oldFile.Click += (_, _) => LoadTextIntoBox(_diffOldBox);
        newFile.Click += (_, _) => LoadTextIntoBox(_diffNewBox);
        currentAsNew.Click += async (_, _) => { if (_diffNewBox != null) _diffNewBox.Text = await GenerateConfigAsync(); };
        selectedAsOld.Click += (_, _) => { if (_diffOldBox != null && _projectDeviceGrid?.SelectedItem is ProjectDeviceSnapshot d) _diffOldBox.Text = d.GeneratedConfiguration; };
        compare.Click += (_, _) => RefreshConfigDiff();
        foreach (var b in new[] { oldFile, newFile, currentAsNew, selectedAsOld, compare }) actions.Children.Add(b);
        Grid.SetRow(actions, 2); root.Children.Add(actions);

        _diffGrid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false };
        _diffGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.anderung"), Binding = new Binding(nameof(ConfigDiffLine.Change)), Width = 100 });
        _diffGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.kontext"), Binding = new Binding(nameof(ConfigDiffLine.Context)), Width = 220 });
        _diffGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.befehl"), Binding = new Binding(nameof(ConfigDiffLine.Line)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _diffGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("common.old"), Binding = new Binding(nameof(ConfigDiffLine.OldLine)), Width = 55 });
        _diffGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.neu"), Binding = new Binding(nameof(ConfigDiffLine.NewLine)), Width = 55 });
        Grid.SetRow(_diffGrid, 3); root.Children.Add(_diffGrid);
        _rollbackBox = CreateCodeBox("Rollback");
        Grid.SetRow(_rollbackBox, 4); root.Children.Add(_rollbackBox);
        tab.Content = root;
        return tab;
    }

    private TabItem BuildGlobalSearchSubTab()
    {
        var tab = new TabItem { Header = LocalizationService.Get("analysis.global_search") };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader("Suche über Module, Felder und Befehle", "Findet Einstellungen und Cisco-Befehle und navigiert direkt zum passenden Eingabefeld."));
        var searchRow = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        var button = new Button { Content = LocalizationService.Get("common.search"), Style = TryFindResource("PrimaryButtonStyle") as Style };
        DockPanel.SetDock(button, Dock.Right);
        _searchBox = new TextBox { MinWidth = 420, Margin = new Thickness(0, 0, 8, 0) };
        _searchBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) RefreshGlobalSearch(); };
        button.Click += (_, _) => RefreshGlobalSearch();
        searchRow.Children.Add(button); searchRow.Children.Add(_searchBox);
        Grid.SetRow(searchRow, 1); root.Children.Add(searchRow);
        _searchGrid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false };
        _searchGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.typ"), Binding = new Binding(nameof(GlobalSearchResult.Kind)), Width = 85 });
        _searchGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("common.results"), Binding = new Binding(nameof(GlobalSearchResult.Title)), Width = 300 });
        _searchGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.beschreibung"), Binding = new Binding(nameof(GlobalSearchResult.Detail)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _searchGrid.MouseDoubleClick += (_, _) => NavigateToSelectedSearchResult();
        Grid.SetRow(_searchGrid, 2); root.Children.Add(_searchGrid);
        tab.Content = root;
        return tab;
    }

    private TabItem BuildAclWorkspaceSubTab()
    {
        var tab = new TabItem { Header = "ACL-Editor" };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader("ACL-Editor und Regelanalyse", "ACLs tabellarisch bearbeiten, aus Projektkonfigurationen importieren und auf Schattenregeln, Redundanzen, breite Freigaben sowie fehlende Interface-Zuordnungen prüfen."));

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        var add = new Button { Content = "Regel hinzufügen", Style = TryFindResource("PrimaryButtonStyle") as Style };
        var remove = new Button { Content = "Regel entfernen" };
        var addBinding = new Button { Content = "Zuordnung hinzufügen" };
        var removeBinding = new Button { Content = "Zuordnung entfernen" };
        var import = new Button { Content = "Aus Projekt importieren" };
        var analyze = new Button { Content = "ACLs analysieren" };
        var copy = new Button { Content = "Konfiguration kopieren" };
        var export = new Button { Content = "CSV exportieren" };
        add.Click += (_, _) => AddAclRule();
        remove.Click += (_, _) => RemoveSelectedAclRule();
        addBinding.Click += (_, _) => AddAclBinding();
        removeBinding.Click += (_, _) => RemoveSelectedAclBinding();
        import.Click += (_, _) => ImportAclWorkspaceFromProject();
        analyze.Click += (_, _) => RefreshAclAnalysis();
        copy.Click += (_, _) => CopyAclConfiguration();
        export.Click += (_, _) => ExportAclCsv();
        foreach (var button in new[] { add, remove, addBinding, removeBinding, import, analyze, copy, export }) actions.Children.Add(button);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        Grid.SetRow(content, 2);
        root.Children.Add(content);

        _aclGrid = new DataGrid
        {
            ItemsSource = _currentProject.AclRules,
            AutoGenerateColumns = false,
            IsReadOnly = false,
            CanUserAddRows = true,
            CanUserDeleteRows = true,
            SelectionMode = DataGridSelectionMode.Single
        };
        _aclGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Aktiv", Binding = new Binding(nameof(ProjectAclRule.Enabled)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 55 });
        _aclGrid.Columns.Add(new DataGridTextColumn { Header = "Gerät", Binding = new Binding(nameof(ProjectAclRule.DeviceName)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 110 });
        _aclGrid.Columns.Add(new DataGridTextColumn { Header = "ACL", Binding = new Binding(nameof(ProjectAclRule.AclName)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 120 });
        _aclGrid.Columns.Add(new DataGridComboBoxColumn { Header = "Familie", SelectedItemBinding = new Binding(nameof(ProjectAclRule.AddressFamily)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, ItemsSource = new[] { "IPv4", "IPv6" }, Width = 70 });
        _aclGrid.Columns.Add(new DataGridComboBoxColumn { Header = "Typ", SelectedItemBinding = new Binding(nameof(ProjectAclRule.AclType)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, ItemsSource = new[] { "Standard", "Extended" }, Width = 80 });
        _aclGrid.Columns.Add(new DataGridTextColumn { Header = "Seq", Binding = new Binding(nameof(ProjectAclRule.Sequence)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 55 });
        _aclGrid.Columns.Add(new DataGridComboBoxColumn { Header = "Aktion", SelectedItemBinding = new Binding(nameof(ProjectAclRule.Action)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, ItemsSource = new[] { "permit", "deny" }, Width = 70 });
        _aclGrid.Columns.Add(new DataGridTextColumn { Header = "Protokoll", Binding = new Binding(nameof(ProjectAclRule.Protocol)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 75 });
        _aclGrid.Columns.Add(new DataGridTextColumn { Header = "Quelle", Binding = new Binding(nameof(ProjectAclRule.Source)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 115 });
        _aclGrid.Columns.Add(new DataGridTextColumn { Header = "Quell-Wildcard", Binding = new Binding(nameof(ProjectAclRule.SourceWildcard)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 105 });
        _aclGrid.Columns.Add(new DataGridTextColumn { Header = "Ziel", Binding = new Binding(nameof(ProjectAclRule.Destination)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 115 });
        _aclGrid.Columns.Add(new DataGridTextColumn { Header = "Ziel-Wildcard", Binding = new Binding(nameof(ProjectAclRule.DestinationWildcard)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 105 });
        _aclGrid.Columns.Add(new DataGridTextColumn { Header = "Dienst / Ports", Binding = new Binding(nameof(ProjectAclRule.Service)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 120 });
        _aclGrid.Columns.Add(new DataGridTextColumn { Header = "Bemerkung", Binding = new Binding(nameof(ProjectAclRule.Remark)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _aclGrid.CellEditEnding += (_, _) => { ScheduleAutoSave(); Dispatcher.BeginInvoke(new Action(RefreshAclAnalysis)); };
        content.Children.Add(_aclGrid);

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        Grid.SetColumn(right, 2);
        content.Children.Add(right);

        _aclFindingsGrid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false };
        _aclFindingsGrid.Columns.Add(new DataGridTextColumn { Header = "Stufe", Binding = new Binding(nameof(AclFinding.Severity)), Width = 70 });
        _aclFindingsGrid.Columns.Add(new DataGridTextColumn { Header = "ACL", Binding = new Binding(nameof(AclFinding.AclName)), Width = 105 });
        _aclFindingsGrid.Columns.Add(new DataGridTextColumn { Header = "Seq", Binding = new Binding(nameof(AclFinding.Sequence)), Width = 50 });
        _aclFindingsGrid.Columns.Add(new DataGridTextColumn { Header = "Feststellung", Binding = new Binding(nameof(AclFinding.Message)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        right.Children.Add(_aclFindingsGrid);

        _aclBindingsGrid = new DataGrid { ItemsSource = _currentProject.AclBindings, IsReadOnly = false, AutoGenerateColumns = false, CanUserAddRows = true, CanUserDeleteRows = true };
        _aclBindingsGrid.Columns.Add(new DataGridTextColumn { Header = "Gerät", Binding = new Binding(nameof(ProjectAclBinding.DeviceName)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 100 });
        _aclBindingsGrid.Columns.Add(new DataGridTextColumn { Header = "Interface", Binding = new Binding(nameof(ProjectAclBinding.Interface)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 110 });
        _aclBindingsGrid.Columns.Add(new DataGridTextColumn { Header = "ACL", Binding = new Binding(nameof(ProjectAclBinding.AclName)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 105 });
        _aclBindingsGrid.Columns.Add(new DataGridComboBoxColumn { Header = "Richtung", SelectedItemBinding = new Binding(nameof(ProjectAclBinding.Direction)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, ItemsSource = new[] { "IN", "OUT" }, Width = 75 });
        _aclBindingsGrid.Columns.Add(new DataGridComboBoxColumn { Header = "Familie", SelectedItemBinding = new Binding(nameof(ProjectAclBinding.AddressFamily)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, ItemsSource = new[] { "IPv4", "IPv6" }, Width = 75 });
        _aclBindingsGrid.CellEditEnding += (_, _) => { ScheduleAutoSave(); Dispatcher.BeginInvoke(new Action(RefreshAclAnalysis)); };
        Grid.SetRow(_aclBindingsGrid, 2);
        right.Children.Add(_aclBindingsGrid);

        _aclPreviewBox = CreateCodeBox("ACL-Konfigurationsvorschau");
        Grid.SetRow(_aclPreviewBox, 4);
        right.Children.Add(_aclPreviewBox);

        tab.Content = root;
        return tab;
    }

    private TabItem BuildCommandAnalysisSubTab()
    {
        var tab = new TabItem { Header = LocalizationService.Get("analysis.command_analysis") };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader("Cisco-Befehl erklären", "Ordnet eine Befehlszeile dem Register zu und erklärt Befehlswörter sowie Parameterpositionen."));
        var inputRow = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        var analyze = new Button { Content = LocalizationService.Get("common.analyze"), Style = TryFindResource("PrimaryButtonStyle") as Style };
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
        var tab = new TabItem { Header = "SSH" };
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
        _sshPortBox = AddAdvancedInlineField(settingsGrid, 0, 1, "Port", _appSettings.DefaultSshPort.ToString(CultureInfo.InvariantCulture), 70);
        _sshUserBox = AddAdvancedInlineField(settingsGrid, 0, 2, "Benutzer", "netadmin", 130);
        _sshAuthModeCombo = new ComboBox { ItemsSource = new[] { "OpenSSH + Schlüssel", "Plink + Passwort" }, SelectedIndex = 0, Width = 170, Margin = new Thickness(6), ItemTemplate = LocalizationService.CreateLocalizedStringTemplate() };
        AddAdvancedInlineControl(settingsGrid, 0, 3, "Authentifizierung", _sshAuthModeCombo);
        _sshDelayBox = AddAdvancedInlineField(settingsGrid, 0, 4, "Zeilen-Delay ms", _appSettings.CommandDelayMilliseconds.ToString(CultureInfo.InvariantCulture), 90);
        _sshSaveCheck = new CheckBox { Content = LocalizationService.Get("text.nach_ubertragung_speichern"), IsChecked = true, Margin = new Thickness(10, 28, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        Grid.SetRow(_sshSaveCheck, 0); Grid.SetColumn(_sshSaveCheck, 5); settingsGrid.Children.Add(_sshSaveCheck);

        _sshKeyBox = AddAdvancedInlineField(settingsGrid, 1, 0, "Private Key", "", 300, 2);
        var browseKey = new Button { Content = LocalizationService.Get("text.key_wahlen"), Margin = new Thickness(6, 27, 6, 6) };
        browseKey.Click += (_, _) => BrowseSshKey();
        Grid.SetRow(browseKey, 1); Grid.SetColumn(browseKey, 2); settingsGrid.Children.Add(browseKey);
        _sshPasswordBox = new PasswordBox { Width = 180, Margin = new Thickness(6), Padding = new Thickness(10, 7, 10, 7), Background = new SolidColorBrush(Color.FromRgb(14, 18, 25)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(39, 46, 58)) };
        AddAdvancedInlineControl(settingsGrid, 1, 3, "Plink-Passwort", _sshPasswordBox);
        var securityNote = new TextBlock { Text = LocalizationService.Get("text.passworter_werden_weder_im_projekt_noch_im_autosave_gespeich"), Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 29, 0, 0) };
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
        var test = new Button { Content = LocalizationService.Get("text.verbindung_testen") };
        var send = new Button { Content = LocalizationService.Get("text.konfiguration_senden"), Style = TryFindResource("PrimaryButtonStyle") as Style };
        var runBackup = new Button { Content = LocalizationService.Get("text.running_sichern") };
        var startBackup = new Button { Content = LocalizationService.Get("text.startup_sichern") };
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
        var exportBackup = new Button { Content = LocalizationService.Get("text.backup_exportieren") };
        var compareBackup = new Button { Content = LocalizationService.Get("text.backup_als_alt_vergleichen") };
        var deleteBackup = new Button { Content = LocalizationService.Get("text.backup_entfernen") };
        exportBackup.Click += (_, _) => ExportSelectedBackup();
        compareBackup.Click += (_, _) => UseSelectedBackupForDiff();
        deleteBackup.Click += (_, _) => DeleteSelectedBackup();
        backupActions.Children.Add(exportBackup); backupActions.Children.Add(compareBackup); backupActions.Children.Add(deleteBackup);
        right.Children.Add(backupActions);
        _backupGrid = new DataGrid { ItemsSource = _currentProject.Backups, IsReadOnly = true, AutoGenerateColumns = false };
        _backupGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("header.device"), Binding = new Binding(nameof(BackupRecord.DeviceName)), Width = 135 });
        _backupGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.typ"), Binding = new Binding(nameof(BackupRecord.BackupType)), Width = 120 });
        _backupGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.zeit"), Binding = new Binding(nameof(BackupRecord.DisplayCreated)), Width = 145 });
        _backupGrid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.quelle"), Binding = new Binding(nameof(BackupRecord.Source)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.SetRow(_backupGrid, 1); right.Children.Add(_backupGrid);
        Grid.SetColumn(right, 2); content.Children.Add(right);

        tab.Content = root;
        _tabsByName["Betrieb"] = tab;
        MainTabs.Items.Add(tab);
    }

    private void BuildDiagramAndReportTab()
    {
        var tab = new TabItem { Header = "Diagramm" };
        var inner = new TabControl();
        inner.Items.Add(BuildDiagramSubTab());
        inner.Items.Add(BuildReportSubTab());
        tab.Content = inner;
        _tabsByName["Diagramm / Bericht"] = tab;
        MainTabs.Items.Add(tab);
    }

    private TabItem BuildDiagramSubTab()
    {
        var tab = new TabItem { Header = LocalizationService.Get("text.netzwerkdiagramm") };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader(
            "Interaktives Netzwerkdiagramm",
            "Geräte können mit der Maus verschoben werden. Verbindungstyp, Interfaces und optionale Beschreibung werden direkt an der Verbindung angezeigt und im Projekt gespeichert."));

        var linkRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 5) };
        _linkSourceCombo = new ComboBox { Width = 155, DisplayMemberPath = nameof(ProjectDeviceSnapshot.Name), ItemsSource = _currentProject.Devices };
        _linkSourceIfBox = new TextBox { Width = 125, Text = "Gi0/0", ToolTip = LocalizationService.Get("diagram.source_interface") };
        _linkTargetCombo = new ComboBox { Width = 155, DisplayMemberPath = nameof(ProjectDeviceSnapshot.Name), ItemsSource = _currentProject.Devices };
        _linkTargetIfBox = new TextBox { Width = 125, Text = "Gi0/0", ToolTip = LocalizationService.Get("diagram.target_interface") };
        _linkTypeCombo = new ComboBox
        {
            Width = 135,
            ItemsSource = new[] { "Ethernet", "Access", "Trunk", "Port-Channel", "Routed Link", "WAN", "Tunnel", "Serial", "Fiber", "Wireless" },
            SelectedIndex = 0,
            ItemTemplate = LocalizationService.CreateLocalizedStringTemplate(),
            ToolTip = LocalizationService.Get("text.verbindungstyp_der_typ_steuert_farbe_und_linienart_im_diagra")
        };
        _linkDescriptionBox = new TextBox { Width = 170, ToolTip = LocalizationService.Get("text.optionale_bezeichnung_z_b_ospf_transit_mpls_core_oder_intern") };
        var addLink = new Button { Content = LocalizationService.Get("text.verbindung_hinzufugen"), Style = TryFindResource("PrimaryButtonStyle") as Style };
        var removeLink = new Button { Content = LocalizationService.Get("text.letzte_entfernen") };
        var refresh = new Button { Content = LocalizationService.Get("text.aktualisieren") };
        var smartLayout = new Button { Content = "Smart Layout", ToolTip = "Ordnet Geräte nach Standort und den Rollen WAN, Core, Distribution und Access an." };
        var gridLayout = new Button { Content = "Raster", ToolTip = LocalizationService.Get("text.verwirft_manuelle_positionen_und_ordnet_alle_gerate_neu_an") };
        var discoveryImport = new Button { Content = "CDP/LLDP importieren", ToolTip = "Ergänzt Verbindungen aus der Ausgabe von show cdp neighbors detail oder show lldp neighbors detail." };
        _routingOverlayCheck = new CheckBox { Content = "Routing-Overlay", IsChecked = _appSettings.ShowRoutingDetails, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 3, 8, 3) };
        var export = new Button { Content = LocalizationService.Get("text.svg_export") };
        addLink.Click += (_, _) => AddProjectLink();
        removeLink.Click += (_, _) => RemoveLastProjectLink();
        refresh.Click += (_, _) => RefreshNetworkDiagram();
        smartLayout.Click += (_, _) => ApplySmartDiagramLayout();
        gridLayout.Click += (_, _) => ResetDiagramLayout();
        discoveryImport.Click += (_, _) => ImportDiscoveryNeighbors();
        _routingOverlayCheck.Checked += (_, _) => { _appSettings.ShowRoutingDetails = true; RefreshNetworkDiagram(); };
        _routingOverlayCheck.Unchecked += (_, _) => { _appSettings.ShowRoutingDetails = false; RefreshNetworkDiagram(); };
        export.Click += (_, _) => ExportNetworkDiagramSvg();
        foreach (var element in new UIElement[]
                 {
                     AdvancedInlineLabel("Quelle"), _linkSourceCombo, _linkSourceIfBox,
                     AdvancedInlineLabel("Ziel"), _linkTargetCombo, _linkTargetIfBox,
                     AdvancedInlineLabel("Typ"), _linkTypeCombo,
                     AdvancedInlineLabel("Bezeichnung"), _linkDescriptionBox,
                     addLink, removeLink, refresh, smartLayout, gridLayout, discoveryImport, _routingOverlayCheck, export
                 })
        {
            if (element is FrameworkElement fe) fe.Margin = new Thickness(3);
            linkRow.Children.Add(element);
        }
        Grid.SetRow(linkRow, 1); root.Children.Add(linkRow);

        var legend = new WrapPanel { Margin = new Thickness(3, 0, 3, 8) };
        legend.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("text.gerate_per_drag_drop_verschieben_verbindungstypen"),
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        foreach (var type in new[] { "Ethernet", "Access", "Trunk", "Port-Channel", "Routed Link", "WAN", "Tunnel", "Serial", "Fiber", "Wireless" })
            legend.Children.Add(CreateDiagramLegendBadge(type));
        Grid.SetRow(legend, 2); root.Children.Add(legend);

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        scroll.SetResourceReference(Control.BackgroundProperty, "WindowBg");
        _diagramCanvas = new Canvas { Width = 1400, Height = 1150, ClipToBounds = true };
        _diagramCanvas.SetResourceReference(Panel.BackgroundProperty, "WindowBg");
        scroll.Content = _diagramCanvas;
        Grid.SetRow(scroll, 3); root.Children.Add(scroll);
        tab.Content = root;
        return tab;
    }

    private Border CreateDiagramLegendBadge(string linkType)
    {
        var color = ParseDiagramColor(NetworkDiagramService.GetLinkColor(linkType));
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Border
        {
            Width = 12,
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        var label = new TextBlock
        {
            Text = linkType,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        content.Children.Add(label);

        var badge = new Border
        {
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(0, 0, 5, 0),
            Child = content
        };
        badge.SetResourceReference(Border.BackgroundProperty, "CardBg");
        return badge;
    }

    private static Color ParseDiagramColor(string value)
    {
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch { return Color.FromRgb(148, 163, 184); }
    }

    private TabItem BuildReportSubTab()
    {
        var tab = new TabItem { Header = "Netzplan" };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CreateAdvancedHeader("Netzplan", "Erzeugt einen exportierbaren Netzplan mit Projektinformationen, grafischer Topologie, Geräten, Verbindungen, VLANs, Subnetzen, Routen, ACLs, VRFs und Prüfungen als HTML, DOCX oder PDF."));
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        var refresh = new Button { Content = LocalizationService.Get("text.vorschau_aktualisieren"), Style = TryFindResource("PrimaryButtonStyle") as Style };
        var html = new Button { Content = LocalizationService.Get("text.html_export") };
        var docx = new Button { Content = LocalizationService.Get("text.docx_export") };
        var pdf = new Button { Content = LocalizationService.Get("text.pdf_export") };
        var package = new Button { Content = "Projektpaket ZIP" };
        refresh.Click += async (_, _) => await RefreshReportPreviewAsync();
        html.Click += async (_, _) => await ExportProjectReportAsync("html");
        docx.Click += async (_, _) => await ExportProjectReportAsync("docx");
        pdf.Click += async (_, _) => await ExportProjectReportAsync("pdf");
        package.Click += async (_, _) => await ExportProjectPackageAsync();
        actions.Children.Add(refresh); actions.Children.Add(html); actions.Children.Add(docx); actions.Children.Add(pdf); actions.Children.Add(package);
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
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.interface"), Binding = new Binding(nameof(PortPlanEntry.Interface)), Width = 145 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.beschreibung"), Binding = new Binding(nameof(PortPlanEntry.Description)), Width = 180 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("header.mode"), Binding = new Binding(nameof(PortPlanEntry.Mode)), Width = 85 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("port.access"), Binding = new Binding(nameof(PortPlanEntry.AccessVlan)), Width = 65 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("port.voice"), Binding = new Binding(nameof(PortPlanEntry.VoiceVlan)), Width = 65 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("port.allowed_vlans"), Binding = new Binding(nameof(PortPlanEntry.AllowedVlans)), Width = 135 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("port.native"), Binding = new Binding(nameof(PortPlanEntry.NativeVlan)), Width = 65 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("port.port_channel"), Binding = new Binding(nameof(PortPlanEntry.ChannelGroup)), Width = 115 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("port.ip_address"), Binding = new Binding(nameof(PortPlanEntry.IpAddress)), Width = 180 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.status"), Binding = new Binding(nameof(PortPlanEntry.State)), Width = 80 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("port.stp_protection"), Binding = new Binding(nameof(PortPlanEntry.StpProtection)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
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
        if (!silent) MessageBox.Show(this, LocalizationService.Format("message.device_added_to_project", snapshot.Name), T("navigation.project"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task UpdateSelectedProjectDeviceAsync()
    {
        if (_projectDeviceGrid?.SelectedItem is not ProjectDeviceSnapshot snapshot)
        {
            MessageBox.Show(this, T("text.bitte_zuerst_ein_projektgerat_auswahlen"), T("navigation.project"), MessageBoxButton.OK, MessageBoxImage.Information);
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
            Site = source.Site,
            TopologyRole = source.TopologyRole,
            DiagramX = source.DiagramX.HasValue ? source.DiagramX.Value + 35 : null,
            DiagramY = source.DiagramY.HasValue ? source.DiagramY.Value + 35 : null,
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
        MessageBox.Show(this, T("text.der_gegenstellenentwurf_wurde_als_projektgerat_angelegt_plat"), T("navigation.peer"), MessageBoxButton.OK, MessageBoxImage.Information);
    }


    private void ShowSelectedProjectDeviceConfiguration()
    {
        if (_projectDeviceGrid?.SelectedItem is not ProjectDeviceSnapshot snapshot)
        {
            MessageBox.Show(this, T("text.bitte_zuerst_ein_projektgerat_auswahlen"), T("navigation.project"), MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show(this, T("text.bitte_zuerst_ein_projektgerat_auswahlen"), T("navigation.project"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(snapshot.GeneratedConfiguration))
        {
            MessageBox.Show(this, T("text.fur_dieses_projektgerat_ist_noch_keine_konfiguration_gespeic"), T("navigation.project"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.Get("text.geratekonfiguration_exportieren"),
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
        foreach (var rule in _currentProject.AclRules.Where(x => x.DeviceName.Equals(snapshot.Name, StringComparison.OrdinalIgnoreCase)).ToList()) _currentProject.AclRules.Remove(rule);
        foreach (var binding in _currentProject.AclBindings.Where(x => x.DeviceName.Equals(snapshot.Name, StringComparison.OrdinalIgnoreCase)).ToList()) _currentProject.AclBindings.Remove(binding);
        RefreshProjectDeviceBindings();
        RefreshNetworkDiagram();
        ScheduleAutoSave();
    }

    private void NewNetworkProject()
    {
        if (MessageBox.Show(this, T("text.aktuelles_projekt_verwerfen_und_ein_neues_projekt_beginnen"), T("text.neues_projekt"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _currentProject = new NetworkProject();
        _currentProjectPath = string.Empty;
        RebindProjectCollections();
        RefreshProjectEditors();
        RefreshNetworkDiagram();
        ScheduleAutoSave();
    }

    private void OpenNetworkProject()
    {
        var dialog = new OpenFileDialog { Title = LocalizationService.Get("text.netzwerkprojekt_offnen"), Filter = "Cisco-Projekt (*.ciscoproject.json)|*.ciscoproject.json|JSON (*.json)|*.json|Alle Dateien (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _currentProject = ProjectService.Load(dialog.FileName);
            _currentProjectPath = dialog.FileName;
            RebindProjectCollections();
            RefreshProjectEditors();
            RefreshNetworkDiagram();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, T("text.projekt_konnte_nicht_geoffnet_werden"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void SaveNetworkProject(bool saveAs)
    {
        SyncProjectEditors();
        if (saveAs || string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            var dialog = new SaveFileDialog { Title = LocalizationService.Get("text.netzwerkprojekt_speichern"), Filter = "Cisco-Projekt (*.ciscoproject.json)|*.ciscoproject.json|JSON (*.json)|*.json", FileName = SanitizeFileName(_currentProject.Name) + ".ciscoproject.json" };
            if (dialog.ShowDialog(this) != true) return;
            _currentProjectPath = dialog.FileName;
        }
        try
        {
            ProjectService.Save(_currentProject, _currentProjectPath);
            MessageBox.Show(this, T("text.projekt_wurde_gespeichert"), T("navigation.project"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, T("text.speicherfehler"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void SyncProjectEditors()
    {
        if (_projectNameBox != null && !string.IsNullOrWhiteSpace(_projectNameBox.Text)) _currentProject.Name = _projectNameBox.Text.Trim();
        if (_projectDescriptionBox != null) _currentProject.Description = _projectDescriptionBox.Text;
        _currentProject.ProjectInfo ??= new ProjectPlanInfo();
        if (_projectNumberBox != null) _currentProject.ProjectInfo.ProjectNumber = _projectNumberBox.Text?.Trim() ?? string.Empty;
        if (_projectCustomerBox != null) _currentProject.ProjectInfo.Customer = _projectCustomerBox.Text?.Trim() ?? string.Empty;
        if (_projectLocationBox != null) _currentProject.ProjectInfo.Location = _projectLocationBox.Text?.Trim() ?? string.Empty;
        if (_projectManagerBox != null) _currentProject.ProjectInfo.ProjectManager = _projectManagerBox.Text?.Trim() ?? string.Empty;
        if (_projectAuthorBox != null) _currentProject.ProjectInfo.Author = _projectAuthorBox.Text?.Trim() ?? string.Empty;
        if (_projectVersionBox != null) _currentProject.ProjectInfo.Version = string.IsNullOrWhiteSpace(_projectVersionBox.Text) ? "1.0" : _projectVersionBox.Text.Trim();
        if (_projectStatusBox != null) _currentProject.ProjectInfo.Status = string.IsNullOrWhiteSpace(_projectStatusBox.Text) ? "Entwurf" : _projectStatusBox.Text.Trim();
        _currentProject.ModifiedUtc = DateTime.UtcNow;
    }

    private void RefreshProjectEditors()
    {
        if (_projectNameBox != null) _projectNameBox.Text = _currentProject.Name;
        if (_projectDescriptionBox != null) _projectDescriptionBox.Text = _currentProject.Description;
        _currentProject.ProjectInfo ??= new ProjectPlanInfo();
        if (_projectNumberBox != null) _projectNumberBox.Text = _currentProject.ProjectInfo.ProjectNumber;
        if (_projectCustomerBox != null) _projectCustomerBox.Text = _currentProject.ProjectInfo.Customer;
        if (_projectLocationBox != null) _projectLocationBox.Text = _currentProject.ProjectInfo.Location;
        if (_projectManagerBox != null) _projectManagerBox.Text = _currentProject.ProjectInfo.ProjectManager;
        if (_projectAuthorBox != null) _projectAuthorBox.Text = _currentProject.ProjectInfo.Author;
        if (_projectVersionBox != null) _projectVersionBox.Text = _currentProject.ProjectInfo.Version;
        if (_projectStatusBox != null) _projectStatusBox.Text = _currentProject.ProjectInfo.Status;
    }

    private void NormalizeProjectCollections()
    {
        _currentProject.Devices ??= new();
        _currentProject.IpamEntries ??= new();
        _currentProject.Links ??= new();
        _currentProject.Backups ??= new();
        _currentProject.AclRules ??= new();
        _currentProject.AclBindings ??= new();
        _currentProject.ProjectInfo ??= new ProjectPlanInfo();
    }

    private void RebindProjectCollections()
    {
        if (_projectDeviceGrid != null) _projectDeviceGrid.ItemsSource = _currentProject.Devices;
        if (_ipamGrid != null) _ipamGrid.ItemsSource = _currentProject.IpamEntries;
        if (_backupGrid != null) _backupGrid.ItemsSource = _currentProject.Backups;
        if (_aclGrid != null) _aclGrid.ItemsSource = _currentProject.AclRules;
        if (_aclBindingsGrid != null) _aclBindingsGrid.ItemsSource = _currentProject.AclBindings;
        RefreshAclAnalysis();
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
        MessageBox.Show(this, LocalizationService.Format("message.ipam_entries_added", added), "IPAM", MessageBoxButton.OK, MessageBoxImage.Information);
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
        var dialog = new SaveFileDialog { Title = LocalizationService.Get("dialog.ipam_export"), Filter = "CSV (*.csv)|*.csv", FileName = "ip_adressplan.csv" };
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
        var dialog = new SaveFileDialog { Title = LocalizationService.Get("dialog.portplan_export"), Filter = "CSV (*.csv)|*.csv", FileName = "portplan.csv" };
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
        MessageBox.Show(this, LocalizationService.Format("message.fixes_applied", fixedCount), T("text.auto_korrektur"), MessageBoxButton.OK, MessageBoxImage.Information);
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
            _sshSaveCheck?.IsChecked == true,
            Math.Clamp(_appSettings.ConnectionTimeoutSeconds, 1, 600),
            Math.Clamp(_appSettings.CommandTimeoutSeconds, 10, 3600),
            _appSettings.AbortTransferOnError);
    }

    private void BrowseSshKey()
    {
        var dialog = new OpenFileDialog { Title = LocalizationService.Get("text.private_ssh_schlusseldatei_wahlen"), Filter = "Schlüsseldateien (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true && _sshKeyBox != null) _sshKeyBox.Text = dialog.FileName;
    }

    private async Task TestSshConnectionAsync()
    {
        if (_operationsOutputBox != null) _operationsOutputBox.Text = LocalizationService.Get("text.verbindung_wird_gepruft");
        var result = await SshDeviceService.TestTcpAsync(GetSshSettings());
        if (_operationsOutputBox != null) _operationsOutputBox.Text = result.Success ? result.Output : result.Error;
    }

    private async Task SendCurrentConfigBySshAsync()
    {
        var settings = GetSshSettings();
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Username))
        {
            MessageBox.Show(this, T("text.host_und_benutzer_mussen_angegeben_werden"), "SSH", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show(this, LocalizationService.Format("message.confirm_ssh_transfer", settings.Host, settings.Port), T("text.ssh_ubertragung"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (_operationsOutputBox != null) _operationsOutputBox.Text = LocalizationService.Get("text.konfiguration_wird_ubertragen");
        var result = await SshDeviceService.SendConfigurationAsync(settings, await GenerateConfigAsync());
        if (_operationsOutputBox != null) _operationsOutputBox.Text = result.Output + (string.IsNullOrWhiteSpace(result.Error) ? "" : "\nFEHLER:\n" + result.Error);
        MessageBox.Show(this, result.Success ? T("text.ubertragung_abgeschlossen") : T("text.ubertragung_fehlgeschlagen_ausgabe_prufen"), "SSH", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
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
        var dialog = new SaveFileDialog { Title = LocalizationService.Get("text.backup_exportieren"), Filter = "Cisco-Konfiguration (*.cfg)|*.cfg|Textdatei (*.txt)|*.txt", FileName = $"{SanitizeFileName(backup.DeviceName)}_{backup.BackupType.Replace("-", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.cfg" };
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

    private void AddAclRule()
    {
        var deviceName = (_projectDeviceGrid?.SelectedItem as ProjectDeviceSnapshot)?.Name
                         ?? _currentProject.Devices.FirstOrDefault()?.Name
                         ?? string.Empty;
        var nextSequence = _currentProject.AclRules
            .Where(rule => rule.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase) && rule.AclName.Equals("ACL-NAME", StringComparison.OrdinalIgnoreCase))
            .Select(rule => rule.Sequence)
            .DefaultIfEmpty(0)
            .Max() + 10;
        var rule = new ProjectAclRule { DeviceName = deviceName, Sequence = Math.Max(10, nextSequence) };
        _currentProject.AclRules.Add(rule);
        _aclGrid?.ScrollIntoView(rule);
        RefreshAclAnalysis();
        ScheduleAutoSave();
    }

    private void RemoveSelectedAclRule()
    {
        if (_aclGrid?.SelectedItem is not ProjectAclRule rule) return;
        _currentProject.AclRules.Remove(rule);
        RefreshAclAnalysis();
        ScheduleAutoSave();
    }

    private void AddAclBinding()
    {
        var selectedRule = _aclGrid?.SelectedItem as ProjectAclRule;
        var binding = new ProjectAclBinding
        {
            DeviceName = selectedRule?.DeviceName ?? _currentProject.Devices.FirstOrDefault()?.Name ?? string.Empty,
            AclName = selectedRule?.AclName ?? string.Empty,
            AddressFamily = selectedRule?.AddressFamily ?? "IPv4",
            Direction = "IN"
        };
        _currentProject.AclBindings.Add(binding);
        _aclBindingsGrid?.ScrollIntoView(binding);
        RefreshAclAnalysis();
        ScheduleAutoSave();
    }

    private void RemoveSelectedAclBinding()
    {
        if (_aclBindingsGrid?.SelectedItem is not ProjectAclBinding binding) return;
        _currentProject.AclBindings.Remove(binding);
        RefreshAclAnalysis();
        ScheduleAutoSave();
    }

    private void ImportAclWorkspaceFromProject()
    {
        var imported = AclWorkspaceService.ImportFromProject(_currentProject);
        _currentProject.AclRules.Clear();
        foreach (var rule in imported.Rules) _currentProject.AclRules.Add(rule);
        _currentProject.AclBindings.Clear();
        foreach (var binding in imported.Bindings) _currentProject.AclBindings.Add(binding);
        RefreshAclAnalysis();
        ScheduleAutoSave();
        MessageBox.Show(this, $"{imported.Rules.Count} ACL-Regeln und {imported.Bindings.Count} Interface-Zuordnungen wurden importiert.", "ACL-Import", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshAclAnalysis()
    {
        if (_aclFindingsGrid != null)
            _aclFindingsGrid.ItemsSource = AclWorkspaceService.Analyze(_currentProject.AclRules, _currentProject.AclBindings);
        if (_aclPreviewBox != null)
            _aclPreviewBox.Text = AclWorkspaceService.BuildConfiguration(_currentProject.AclRules, _currentProject.AclBindings);
    }

    private void CopyAclConfiguration()
    {
        var configuration = AclWorkspaceService.BuildConfiguration(_currentProject.AclRules, _currentProject.AclBindings);
        if (string.IsNullOrWhiteSpace(configuration))
        {
            MessageBox.Show(this, "Es sind keine aktiven ACL-Regeln vorhanden.", "ACL-Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Clipboard.SetText(configuration);
        if (_aclPreviewBox != null) _aclPreviewBox.Text = configuration;
    }

    private void ExportAclCsv()
    {
        var dialog = new SaveFileDialog { Title = "ACL-Regeln exportieren", Filter = "CSV (*.csv)|*.csv", FileName = "acl-regeln.csv" };
        if (dialog.ShowDialog(this) != true) return;
        var lines = new List<string> { "Device;ACL;Family;Type;Sequence;Action;Protocol;Source;SourceWildcard;Destination;DestinationWildcard;Service;Remark;Enabled" };
        lines.AddRange(_currentProject.AclRules.Select(rule => string.Join(";",
            CsvValue(rule.DeviceName), CsvValue(rule.AclName), CsvValue(rule.AddressFamily), CsvValue(rule.AclType), rule.Sequence.ToString(CultureInfo.InvariantCulture),
            CsvValue(rule.Action), CsvValue(rule.Protocol), CsvValue(rule.Source), CsvValue(rule.SourceWildcard),
            CsvValue(rule.Destination), CsvValue(rule.DestinationWildcard), CsvValue(rule.Service), CsvValue(rule.Remark), rule.Enabled.ToString())));
        File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
    }

    private static string CsvValue(object? value) => '"' + (value?.ToString() ?? string.Empty).Replace("\"", "\"\"") + '"';

    private void ApplySmartDiagramLayout()
    {
        if (_diagramCanvas == null) return;
        var siteCount = Math.Max(1, _currentProject.Devices.Select(TopologyPlanningService.InferSite).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var maxDevicesPerSiteRole = _currentProject.Devices
            .GroupBy(device => $"{TopologyPlanningService.InferSite(device)}|{TopologyPlanningService.InferRole(device)}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Count())
            .DefaultIfEmpty(1)
            .Max();
        _diagramCanvas.Width = Math.Max(1400, siteCount * Math.Max(320, maxDevicesPerSiteRole * 250));
        _diagramCanvas.Height = 1150;
        TopologyPlanningService.ApplySmartLayout(_currentProject, _diagramCanvas.Width, _diagramCanvas.Height);
        RefreshNetworkDiagram();
        _projectDeviceGrid?.Items.Refresh();
        ScheduleAutoSave();
    }

    private void ImportDiscoveryNeighbors()
    {
        if (_currentProject.Devices.Count == 0)
        {
            MessageBox.Show(this, "Zuerst müssen Projektgeräte vorhanden sein.", "CDP/LLDP-Import", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Window
        {
            Title = "CDP-/LLDP-Nachbarn importieren",
            Owner = this,
            Width = 760,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(11, 14, 19))
        };
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Quellgerät auswählen und die Ausgabe von 'show cdp neighbors detail' oder 'show lldp neighbors detail' einfügen.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });
        var sourceCombo = new ComboBox
        {
            ItemsSource = _currentProject.Devices,
            DisplayMemberPath = nameof(ProjectDeviceSnapshot.Name),
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(sourceCombo, 1);
        root.Children.Add(sourceCombo);

        var input = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas")
        };
        Grid.SetRow(input, 2);
        root.Children.Add(input);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var cancel = new Button { Content = "Abbrechen" };
        var import = new Button { Content = "Verbindungen importieren", Style = TryFindResource("PrimaryButtonStyle") as Style };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        import.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(cancel);
        actions.Children.Add(import);
        Grid.SetRow(actions, 3);
        root.Children.Add(actions);
        dialog.Content = root;

        if (dialog.ShowDialog() != true || sourceCombo.SelectedItem is not ProjectDeviceSnapshot source) return;
        var result = TopologyPlanningService.AddDiscoveredLinks(_currentProject, source, input.Text);
        RefreshProjectDeviceBindings();
        ApplySmartDiagramLayout();
        var unresolved = result.UnresolvedDevices.Count == 0 ? "Keine." : string.Join(", ", result.UnresolvedDevices);
        MessageBox.Show(this,
            $"Neue Verbindungen: {result.AddedLinks}\nÜbersprungen: {result.SkippedLinks}\nNicht zugeordnete Geräte: {unresolved}",
            "CDP/LLDP-Import", MessageBoxButton.OK,
            result.UnresolvedDevices.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async Task ExportProjectPackageAsync()
    {
        await EnsureProjectContainsCurrentDeviceAsync();
        SyncProjectEditors();
        _advancedDependencyFindings = DependencyValidationService.Analyze(BuildRequest());
        _advancedSecurityFindings = SecurityAuditService.Analyze(await GenerateConfigAsync());
        if (_currentProject.AclRules.Count == 0)
        {
            var imported = AclWorkspaceService.ImportFromProject(_currentProject);
            _currentProject.AclBindings.Clear();
            foreach (var rule in imported.Rules) _currentProject.AclRules.Add(rule);
            foreach (var binding in imported.Bindings) _currentProject.AclBindings.Add(binding);
        }

        var dialog = new SaveFileDialog
        {
            Title = "Projektpaket exportieren",
            Filter = "ZIP-Archiv (*.zip)|*.zip",
            FileName = SanitizeFileName(_currentProject.Name) + "_Projektpaket.zip"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ProjectPackageExportService.Export(dialog.FileName, _currentProject, _advancedDependencyFindings, _advancedSecurityFindings);
            MessageBox.Show(this, "Das Projektpaket wurde vollständig exportiert.", "Projektpaket", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Projektpaket konnte nicht exportiert werden", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddProjectLink()
    {
        if (_linkSourceCombo?.SelectedItem is not ProjectDeviceSnapshot source || _linkTargetCombo?.SelectedItem is not ProjectDeviceSnapshot target)
        {
            MessageBox.Show(this, T("text.quelle_und_ziel_mussen_ausgewahlt_werden"), T("common.connection"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (source.Id == target.Id)
        {
            MessageBox.Show(this, T("text.quelle_und_ziel_durfen_nicht_identisch_sein"), T("common.connection"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sourceInterface = _linkSourceIfBox?.Text.Trim() ?? string.Empty;
        var targetInterface = _linkTargetIfBox?.Text.Trim() ?? string.Empty;
        var linkType = _linkTypeCombo?.SelectedItem?.ToString() ?? "Ethernet";
        if (_currentProject.Links.Any(x =>
                x.SourceDeviceId.Equals(source.Id, StringComparison.OrdinalIgnoreCase) &&
                x.TargetDeviceId.Equals(target.Id, StringComparison.OrdinalIgnoreCase) &&
                x.SourceInterface.Equals(sourceInterface, StringComparison.OrdinalIgnoreCase) &&
                x.TargetInterface.Equals(targetInterface, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, T("text.diese_verbindung_ist_bereits_im_projekt_vorhanden"), T("common.connection"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _currentProject.Links.Add(new ProjectLink
        {
            SourceDeviceId = source.Id,
            TargetDeviceId = target.Id,
            SourceInterface = sourceInterface,
            TargetInterface = targetInterface,
            LinkType = linkType,
            Description = _linkDescriptionBox?.Text.Trim() ?? string.Empty
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

    private void ResetDiagramLayout()
    {
        foreach (var device in _currentProject.Devices)
        {
            device.DiagramX = null;
            device.DiagramY = null;
        }
        RefreshNetworkDiagram();
        ScheduleAutoSave();
    }

    private void RefreshNetworkDiagram()
    {
        if (_diagramCanvas == null) return;
        _diagramCanvas.Children.Clear();
        _diagramDeviceElements.Clear();
        _diagramLinkElements.Clear();

        var layout = NetworkDiagramService.CalculateLayout(_currentProject, _diagramCanvas.Width, _diagramCanvas.Height);
        foreach (var link in _currentProject.Links)
        {
            if (!layout.TryGetValue(link.SourceDeviceId, out var source) || !layout.TryGetValue(link.TargetDeviceId, out var target)) continue;
            AddDiagramLinkVisual(link, source, target);
        }

        foreach (var device in _currentProject.Devices)
        {
            if (!layout.TryGetValue(device.Id, out var position)) continue;
            var border = CreateDiagramDeviceElement(device, position);
            _diagramDeviceElements[device.Id] = border;
            Canvas.SetLeft(border, position.X);
            Canvas.SetTop(border, position.Y);
            Panel.SetZIndex(border, 10);
            _diagramCanvas.Children.Add(border);
        }
        UpdateDiagramConnections();
    }

    private Border CreateDiagramDeviceElement(ProjectDeviceSnapshot device, NetworkDiagramService.DiagramPoint position)
    {
        var (deviceLabel, borderColor, backgroundColor) = GetDiagramDeviceStyle(device.DeviceType);
        var title = new TextBlock
        {
            Text = device.Name,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var type = new TextBlock
        {
            Text = $"{deviceLabel} · {device.DeviceType}",
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0)
        };
        var mode = new TextBlock
        {
            Text = device.ConfigMode,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0)
        };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(CreateDiagramDeviceIcon(device.DeviceType, borderColor));
        title.Margin = new Thickness(0, 3, 0, 0);
        stack.Children.Add(title);
        stack.Children.Add(type);
        stack.Children.Add(mode);
        stack.Children.Add(new TextBlock
        {
            Text = $"{TopologyPlanningService.InferRole(device)} · {TopologyPlanningService.InferSite(device)}",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        });
        var extraDetails = BuildDeviceDiagramDetails(device);
        if (!string.IsNullOrWhiteSpace(extraDetails))
        {
            stack.Children.Add(new TextBlock
            {
                Text = extraDetails,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 3, 4, 0)
            });
        }

        var border = new Border
        {
            Width = position.Width,
            Height = position.Height,
            CornerRadius = new CornerRadius(deviceLabel == "RT" ? 28 : 12),
            Background = new SolidColorBrush(backgroundColor),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(2),
            Child = stack,
            Cursor = Cursors.SizeAll,
            ToolTip = $"{device.Name}\n{deviceLabel} · {device.DeviceType}\n{device.ConfigMode}\n\nMit gedrückter linker Maustaste verschieben."
        };
        border.MouseLeftButtonDown += (_, e) => BeginDiagramDeviceDrag(device, border, e);
        border.MouseMove += (_, e) => MoveDiagramDevice(device, border, e);
        border.MouseLeftButtonUp += (_, e) => EndDiagramDeviceDrag(device, border, e);
        border.LostMouseCapture += (_, _) =>
        {
            if (ReferenceEquals(_draggedDiagramElement, border))
            {
                _draggedDiagramDevice = null;
                _draggedDiagramElement = null;
            }
        };
        return border;
    }

    private static Viewbox CreateDiagramDeviceIcon(string? deviceType, Color accentColor)
    {
        var normalized = (deviceType ?? string.Empty).Trim();
        var stroke = new SolidColorBrush(accentColor);
        var canvas = new Canvas { Width = 48, Height = 36 };

        if (normalized.Contains("L3", StringComparison.OrdinalIgnoreCase))
        {
            var body = new Rectangle
            {
                Width = 44,
                Height = 30,
                RadiusX = 5,
                RadiusY = 5,
                Stroke = stroke,
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(body, 2);
            Canvas.SetTop(body, 3);
            canvas.Children.Add(body);

            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Stroke = stroke,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = Geometry.Parse("M 8,12 L 40,12 M 35,8 L 40,12 L 35,16 M 13,8 L 8,12 L 13,16 M 24,7 L 24,29 M 20,11 L 24,7 L 28,11 M 20,25 L 24,29 L 28,25")
            });
        }
        else if (normalized.Contains("L2", StringComparison.OrdinalIgnoreCase))
        {
            var body = new Rectangle
            {
                Width = 44,
                Height = 30,
                RadiusX = 4,
                RadiusY = 4,
                Stroke = stroke,
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(body, 2);
            Canvas.SetTop(body, 3);
            canvas.Children.Add(body);

            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Stroke = stroke,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = Geometry.Parse("M 8,12 L 40,12 M 35,8 L 40,12 L 35,16 M 13,8 L 8,12 L 13,16 M 8,24 L 40,24 M 35,20 L 40,24 L 35,28 M 13,20 L 8,24 L 13,28")
            });
        }
        else
        {
            var body = new Ellipse
            {
                Width = 34,
                Height = 34,
                Stroke = stroke,
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(body, 7);
            Canvas.SetTop(body, 1);
            canvas.Children.Add(body);

            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Stroke = stroke,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = Geometry.Parse("M 11,18 L 37,18 M 32,13 L 37,18 L 32,23 M 16,13 L 11,18 L 16,23 M 24,5 L 24,31 M 19,10 L 24,5 L 29,10 M 19,26 L 24,31 L 29,26")
            });
        }

        return new Viewbox
        {
            Width = 40,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = canvas
        };
    }

    private static (string Label, Color BorderColor, Color BackgroundColor) GetDiagramDeviceStyle(string? deviceType)
    {
        var normalized = (deviceType ?? string.Empty).Trim();
        if (normalized.Contains("L3", StringComparison.OrdinalIgnoreCase))
            return ("L3SW", Color.FromRgb(45, 212, 191), Color.FromRgb(18, 34, 36));
        if (normalized.Contains("L2", StringComparison.OrdinalIgnoreCase))
            return ("L2SW", Color.FromRgb(74, 222, 128), Color.FromRgb(19, 33, 24));
        return ("RT", Color.FromRgb(232, 121, 26), Color.FromRgb(36, 24, 15));
    }

    private void BeginDiagramDeviceDrag(ProjectDeviceSnapshot device, Border element, MouseButtonEventArgs e)
    {
        if (_diagramCanvas == null) return;
        _draggedDiagramDevice = device;
        _draggedDiagramElement = element;
        var pointer = e.GetPosition(_diagramCanvas);
        _diagramDragOffset = new Point(pointer.X - Canvas.GetLeft(element), pointer.Y - Canvas.GetTop(element));
        element.CaptureMouse();
        e.Handled = true;
    }

    private void MoveDiagramDevice(ProjectDeviceSnapshot device, Border element, MouseEventArgs e)
    {
        if (_diagramCanvas == null || e.LeftButton != MouseButtonState.Pressed ||
            !ReferenceEquals(_draggedDiagramDevice, device) || !ReferenceEquals(_draggedDiagramElement, element)) return;

        var pointer = e.GetPosition(_diagramCanvas);
        var x = Math.Clamp(pointer.X - _diagramDragOffset.X, 5, Math.Max(5, _diagramCanvas.Width - element.Width - 5));
        var y = Math.Clamp(pointer.Y - _diagramDragOffset.Y, 5, Math.Max(5, _diagramCanvas.Height - element.Height - 5));
        if (_appSettings.SnapDiagramToGrid)
        {
            const double gridSize = 20;
            x = Math.Round(x / gridSize) * gridSize;
            y = Math.Round(y / gridSize) * gridSize;
        }
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        device.DiagramX = x;
        device.DiagramY = y;
        UpdateDiagramConnections();
        e.Handled = true;
    }

    private void EndDiagramDeviceDrag(ProjectDeviceSnapshot device, Border element, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(_draggedDiagramDevice, device) || !ReferenceEquals(_draggedDiagramElement, element)) return;
        element.ReleaseMouseCapture();
        _draggedDiagramDevice = null;
        _draggedDiagramElement = null;
        ScheduleAutoSave();
        e.Handled = true;
    }

    private void AddDiagramLinkVisual(ProjectLink link, NetworkDiagramService.DiagramPoint source, NetworkDiagramService.DiagramPoint target)
    {
        if (_diagramCanvas == null) return;
        var color = ParseDiagramColor(NetworkDiagramService.GetLinkColor(link.LinkType));
        var brush = new SolidColorBrush(color);
        var line = new Line
        {
            X1 = source.X + source.Width / 2,
            Y1 = source.Y + source.Height / 2,
            X2 = target.X + target.Width / 2,
            Y2 = target.Y + target.Height / 2,
            Stroke = brush,
            StrokeThickness = NetworkDiagramService.GetLinkThickness(link.LinkType),
            SnapsToDevicePixels = true
        };
        var dash = NetworkDiagramService.GetLinkDashArray(link.LinkType);
        if (!string.IsNullOrWhiteSpace(dash))
            line.StrokeDashArray = new DoubleCollection(dash.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(double.Parse));

        var label = new TextBlock
        {
            Text = BuildDiagramLinkLabel(link),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 250
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var labelBorder = new Border
        {
            Child = label,
            BorderBrush = brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 3, 6, 3),
            ToolTip = BuildDiagramLinkToolTip(link)
        };
        labelBorder.SetResourceReference(Border.BackgroundProperty, "CardBg");
        var sourceEndpoint = new Ellipse { Width = 10, Height = 10, Fill = brush, Stroke = new SolidColorBrush(Color.FromRgb(11, 14, 19)), StrokeThickness = 1 };
        var targetEndpoint = new Ellipse { Width = 10, Height = 10, Fill = brush, Stroke = new SolidColorBrush(Color.FromRgb(11, 14, 19)), StrokeThickness = 1 };
        Panel.SetZIndex(line, 0);
        Panel.SetZIndex(sourceEndpoint, 1);
        Panel.SetZIndex(targetEndpoint, 1);
        Panel.SetZIndex(labelBorder, 2);
        _diagramCanvas.Children.Add(line);
        _diagramCanvas.Children.Add(sourceEndpoint);
        _diagramCanvas.Children.Add(targetEndpoint);
        _diagramCanvas.Children.Add(labelBorder);
        _diagramLinkElements.Add(new DiagramLinkVisual
        {
            Link = link,
            Line = line,
            Label = label,
            LabelBorder = labelBorder,
            SourceEndpoint = sourceEndpoint,
            TargetEndpoint = targetEndpoint
        });
    }

    private void UpdateDiagramConnections()
    {
        foreach (var visual in _diagramLinkElements)
        {
            if (!_diagramDeviceElements.TryGetValue(visual.Link.SourceDeviceId, out var source) ||
                !_diagramDeviceElements.TryGetValue(visual.Link.TargetDeviceId, out var target)) continue;

            var x1 = Canvas.GetLeft(source) + source.Width / 2;
            var y1 = Canvas.GetTop(source) + source.Height / 2;
            var x2 = Canvas.GetLeft(target) + target.Width / 2;
            var y2 = Canvas.GetTop(target) + target.Height / 2;
            visual.Line.X1 = x1;
            visual.Line.Y1 = y1;
            visual.Line.X2 = x2;
            visual.Line.Y2 = y2;
            Canvas.SetLeft(visual.SourceEndpoint, x1 - visual.SourceEndpoint.Width / 2);
            Canvas.SetTop(visual.SourceEndpoint, y1 - visual.SourceEndpoint.Height / 2);
            Canvas.SetLeft(visual.TargetEndpoint, x2 - visual.TargetEndpoint.Width / 2);
            Canvas.SetTop(visual.TargetEndpoint, y2 - visual.TargetEndpoint.Height / 2);

            visual.LabelBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var labelWidth = Math.Max(80, visual.LabelBorder.DesiredSize.Width);
            var labelHeight = Math.Max(24, visual.LabelBorder.DesiredSize.Height);
            Canvas.SetLeft(visual.LabelBorder, (x1 + x2) / 2 - labelWidth / 2);
            Canvas.SetTop(visual.LabelBorder, (y1 + y2) / 2 - labelHeight - 8);
        }
    }

    private string BuildDiagramLinkLabel(ProjectLink link)
    {
        var lines = new List<string>();
        var type = string.IsNullOrWhiteSpace(link.LinkType) ? "Ethernet" : link.LinkType.Trim();
        if (_appSettings.ShowConnectionTypes)
            lines.Add(string.IsNullOrWhiteSpace(link.Description) ? type : $"{type}: {link.Description.Trim()}");
        else if (!string.IsNullOrWhiteSpace(link.Description))
            lines.Add(link.Description.Trim());
        if (_appSettings.ShowInterfaceNames)
            lines.Add($"{link.SourceInterface} ↔ {link.TargetInterface}");
        if (_appSettings.ShowRoutingDetails)
        {
            var routing = RoutingVisualizationService.GetLinkSummary(_currentProject, link);
            if (!string.IsNullOrWhiteSpace(routing)) lines.Add(routing);
        }
        return lines.Count == 0 ? LocalizationService.TranslateText("Verbindung") : string.Join("\n", lines);
    }

    private string BuildDeviceDiagramDetails(ProjectDeviceSnapshot device)
    {
        var details = new List<string>();
        var config = device.GeneratedConfiguration ?? string.Empty;
        if (_appSettings.ShowIpAddresses)
        {
            var ip = Regex.Matches(config, @"(?im)^\s*(?:ip address|ipv6 address)\s+([^\s]+(?:\s+[^\s]+)?)")
                .Select(x => x.Groups[1].Value.Trim())
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(ip)) details.Add(ip);
        }
        if (_appSettings.ShowVlans)
        {
            var vlans = Regex.Matches(config, @"(?im)^\s*vlan\s+([0-9,\-]+)")
                .Select(x => x.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            if (vlans.Count > 0) details.Add("VLAN " + string.Join(",", vlans));
        }
        if (_appSettings.ShowRoutingDetails)
            details.AddRange(RoutingVisualizationService.GetDeviceDetails(device).Take(3));
        return string.Join("\n", details.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private string BuildDiagramLinkToolTip(ProjectLink link)
    {
        var source = _currentProject.Devices.FirstOrDefault(x => x.Id == link.SourceDeviceId)?.Name ?? "Quelle";
        var target = _currentProject.Devices.FirstOrDefault(x => x.Id == link.TargetDeviceId)?.Name ?? "Ziel";
        var routing = _appSettings.ShowRoutingDetails ? RoutingVisualizationService.GetLinkSummary(_currentProject, link) : string.Empty;
        return $"{link.LinkType}\n{source} {link.SourceInterface} ↔ {target} {link.TargetInterface}" +
               (string.IsNullOrWhiteSpace(link.Description) ? string.Empty : $"\n{link.Description.Trim()}") +
               (string.IsNullOrWhiteSpace(routing) ? string.Empty : $"\n{routing}");
    }

    private void ExportNetworkDiagramSvg()
    {
        var dialog = new SaveFileDialog { Title = LocalizationService.Get("text.netzwerkdiagramm_exportieren"), Filter = "SVG (*.svg)|*.svg", FileName = "netzwerkdiagramm.svg" };
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
        var dialog = new SaveFileDialog { Title = "Netzplan exportieren", Filter = filter, FileName = SanitizeFileName(_currentProject.Name) + "_Netzplan." + extension };
        if (dialog.ShowDialog(this) != true) return;
        var plain = _reportPreviewBox?.Text ?? string.Empty;
        try
        {
            if (extension == "html") ReportExportService.ExportHtml(dialog.FileName, _currentProject, _advancedDependencyFindings, _advancedSecurityFindings);
            else if (extension == "docx") ReportExportService.ExportDocx(dialog.FileName, plain);
            else ReportExportService.ExportPdf(dialog.FileName, plain);
            MessageBox.Show(this, "Netzplan wurde exportiert.", "Netzplan", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, T("text.exportfehler"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task EnsureProjectContainsCurrentDeviceAsync()
    {
        var hostname = GetFieldValue("hostname");
        if (_currentProject.Devices.Count == 0 || (!string.IsNullOrWhiteSpace(hostname) && !_currentProject.Devices.Any(x => x.Name.Equals(hostname, StringComparison.OrdinalIgnoreCase))))
            await CaptureCurrentDeviceAsync(true);
    }

    private void InitializeAutoSave()
    {
        _autoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(_appSettings.AutoSaveIntervalSeconds, 10, 3600))
        };
        _autoSaveTimer.Tick += (_, _) => SaveAutoSaveState();
        Loaded += (_, _) =>
        {
            if (_appSettings.LoadLastProject)
                TryRestoreAutoSaveState(promptUser: false);
            else if (_appSettings.RestoreAfterCrash)
                TryRestoreAutoSaveState(promptUser: true);

            if (_appSettings.AutoSaveEnabled) _autoSaveTimer?.Start();
        };
        Closing += (_, _) =>
        {
            if (_appSettings.SaveProjectOnExit || _appSettings.AutoSaveEnabled) SaveAutoSaveState();
        };
    }

    private void ScheduleAutoSave()
    {
        if (_autoSaveTimer == null || !_appSettings.AutoSaveEnabled) return;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_appSettings.AutoSaveIntervalSeconds, 10, 3600));
        _autoSaveTimer.Start();
    }

    private void SaveAutoSaveState()
    {
        if (!_appSettings.AutoSaveEnabled && !_appSettings.SaveProjectOnExit) return;
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
            if (_autoSaveTimer != null) _autoSaveTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_appSettings.AutoSaveIntervalSeconds, 10, 3600));
        }
        catch
        {
            // Autosave darf die Bedienung nicht unterbrechen.
        }
    }

    private void TryRestoreAutoSaveState(bool promptUser)
    {
        if (!_appSettings.LoadLastProject && !_appSettings.RestoreAfterCrash) return;
        var path = ProjectService.AutoSavePath;
        if (!File.Exists(path)) return;
        try
        {
            var state = JsonSerializer.Deserialize<AutoSaveState>(File.ReadAllText(path, Encoding.UTF8), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (state == null || DateTime.UtcNow - state.SavedUtc > TimeSpan.FromDays(30)) return;
            if (promptUser && MessageBox.Show(this,
                    LocalizationService.TranslateText($"Es wurde ein Autosave vom {state.SavedUtc.ToLocalTime():dd.MM.yyyy HH:mm} gefunden. Wiederherstellen?"),
                    "Autosave", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
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
            Text = LocalizationService.Get("preview.activate_or_refresh"),
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

        var refresh = new Button { Content = LocalizationService.Get("text.aktualisieren"), Style = TryFindResource("SmallButtonStyle") as Style, HorizontalAlignment = HorizontalAlignment.Right };
        refresh.Click += async (_, _) => await RefreshModuleLivePreviewAsync(module.Name);
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(refresh);
        Grid.SetRow(box, 1);
        panel.Children.Add(box);
        return new Expander
        {
            Header = LocalizationService.Get("preview.module_commands"),
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
            box.Text = LocalizationService.Get("preview.module_inactive");
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
        catch (Exception ex) { box.Text = LocalizationService.Get("preview.error_prefix") + ex.Message; }
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
