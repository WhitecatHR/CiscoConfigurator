using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CiscoConfigGuiWpf;

public partial class MainWindow
{
    private TextBlock? _inventoryProgressText;
    private TextBox? _inventorySummaryBox;
    private DataGrid? _inventoryInterfaceGrid;
    private DataGrid? _inventoryVlanGrid;
    private DataGrid? _inventoryNeighborGrid;
    private TextBox? _inventoryRawBox;
    private SshInventoryCollectionResult? _lastInventoryResult;
    private CancellationTokenSource? _inventoryCancellation;

    private TabItem BuildSshInventorySubTab()
    {
        var tab = new TabItem { Header = LocalizationService.Get("inventory.tab", "Inventarisierung") };
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(160) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        root.Children.Add(CreateAdvancedHeader(
            LocalizationService.Get("inventory.title", "SSH-Live-Inventarisierung"),
            LocalizationService.Get("inventory.description", "Liest Modell, Seriennummer, Softwarestand, Interfaces, VLANs, Trunks, Port-Channels, Nachbarn und Routinginformationen direkt vom Cisco-Gerät. Die Zugangsdaten aus dem Reiter Übertragung & Backups werden verwendet.")));

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        var start = new Button { Content = LocalizationService.Get("inventory.start", "Inventarisierung starten"), Style = TryFindResource("PrimaryButtonStyle") as Style };
        var cancel = new Button { Content = LocalizationService.Get("common.cancel", "Abbrechen") };
        var apply = new Button { Content = LocalizationService.Get("inventory.apply_device", "Als Projektgerät übernehmen") };
        var importIpam = new Button { Content = LocalizationService.Get("inventory.import_ipam", "IPAM übernehmen") };
        var importNeighbors = new Button { Content = LocalizationService.Get("inventory.import_neighbors", "Nachbarn übernehmen") };
        var export = new Button { Content = LocalizationService.Get("inventory.export", "Inventar exportieren") };
        start.Click += async (_, _) => await StartSshInventoryAsync();
        cancel.Click += (_, _) => _inventoryCancellation?.Cancel();
        apply.Click += (_, _) => ApplyInventoryToProject();
        importIpam.Click += (_, _) => ImportInventoryIntoIpam();
        importNeighbors.Click += (_, _) => ImportInventoryNeighbors();
        export.Click += (_, _) => ExportInventoryJson();
        foreach (var button in new[] { start, cancel, apply, importIpam, importNeighbors, export })
        {
            button.Margin = new Thickness(3);
            actions.Children.Add(button);
        }
        _inventoryProgressText = new TextBlock
        {
            Text = LocalizationService.Get("inventory.ready", "Bereit."),
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 6, 0, 0)
        };
        actions.Children.Add(_inventoryProgressText);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);

        _inventorySummaryBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 25)),
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 56, 70)),
            Padding = new Thickness(10),
            Text = LocalizationService.Get("inventory.no_data", "Noch keine Inventarisierung durchgeführt.")
        };
        Grid.SetRow(_inventorySummaryBox, 2);
        root.Children.Add(_inventorySummaryBox);

        var details = new TabControl { Margin = new Thickness(0, 8, 0, 0) };
        _inventoryInterfaceGrid = CreateInventoryInterfaceGrid();
        _inventoryVlanGrid = CreateInventoryVlanGrid();
        _inventoryNeighborGrid = CreateInventoryNeighborGrid();
        _inventoryRawBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 25)),
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            Padding = new Thickness(8)
        };
        details.Items.Add(new TabItem { Header = LocalizationService.Get("inventory.interfaces", "Interfaces"), Content = _inventoryInterfaceGrid });
        details.Items.Add(new TabItem { Header = "VLANs", Content = _inventoryVlanGrid });
        details.Items.Add(new TabItem { Header = LocalizationService.Get("inventory.neighbors", "Nachbarn"), Content = _inventoryNeighborGrid });
        details.Items.Add(new TabItem { Header = LocalizationService.Get("inventory.raw", "Rohdaten"), Content = _inventoryRawBox });
        Grid.SetRow(details, 3);
        root.Children.Add(details);

        tab.Content = root;
        return tab;
    }

    private DataGrid CreateInventoryInterfaceGrid()
    {
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false };
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.interface", "Interface"), Binding = new Binding(nameof(InventoryInterfaceEntry.Name)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "IPv4", Binding = new Binding(nameof(InventoryInterfaceEntry.IpAddress)), Width = 135 });
        grid.Columns.Add(new DataGridTextColumn { Header = "IPv6", Binding = new Binding(nameof(InventoryInterfaceEntry.Ipv6Address)), Width = 180 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.status", "Status"), Binding = new Binding(nameof(InventoryInterfaceEntry.Status)), Width = 120 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("inventory.protocol", "Protokoll"), Binding = new Binding(nameof(InventoryInterfaceEntry.Protocol)), Width = 90 });
        grid.Columns.Add(new DataGridTextColumn { Header = "VLAN", Binding = new Binding(nameof(InventoryInterfaceEntry.Vlan)), Width = 80 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.beschreibung", "Beschreibung"), Binding = new Binding(nameof(InventoryInterfaceEntry.Description)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        return grid;
    }

    private DataGrid CreateInventoryVlanGrid()
    {
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false };
        grid.Columns.Add(new DataGridTextColumn { Header = "VLAN", Binding = new Binding(nameof(InventoryVlanEntry.VlanId)), Width = 80 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("plugins.name", "Name"), Binding = new Binding(nameof(InventoryVlanEntry.Name)), Width = 180 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("text.status", "Status"), Binding = new Binding(nameof(InventoryVlanEntry.Status)), Width = 110 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("inventory.ports", "Ports"), Binding = new Binding(nameof(InventoryVlanEntry.Ports)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        return grid;
    }

    private DataGrid CreateInventoryNeighborGrid()
    {
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false };
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("inventory.protocol", "Protokoll"), Binding = new Binding(nameof(InventoryNeighborEntry.Protocol)), Width = 90 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("inventory.remote_device", "Nachbargerät"), Binding = new Binding(nameof(InventoryNeighborEntry.RemoteDevice)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("inventory.local_interface", "Lokales Interface"), Binding = new Binding(nameof(InventoryNeighborEntry.LocalInterface)), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = LocalizationService.Get("inventory.remote_interface", "Entferntes Interface"), Binding = new Binding(nameof(InventoryNeighborEntry.RemoteInterface)), Width = 160 });
        return grid;
    }

    private async Task StartSshInventoryAsync()
    {
        var settings = GetSshSettings();
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.Username))
        {
            MessageBox.Show(this,
                LocalizationService.Get("text.host_und_benutzer_mussen_angegeben_werden", "Host und Benutzer müssen angegeben werden."),
                LocalizationService.Get("inventory.title", "SSH-Live-Inventarisierung"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _inventoryCancellation?.Cancel();
        _inventoryCancellation?.Dispose();
        _inventoryCancellation = new CancellationTokenSource();
        if (_inventorySummaryBox != null) _inventorySummaryBox.Text = LocalizationService.Get("inventory.running", "Inventarisierung wird ausgeführt ...");
        var progress = new Progress<string>(message => { if (_inventoryProgressText != null) _inventoryProgressText.Text = message; });
        try
        {
            _lastInventoryResult = await SshInventoryService.CollectAsync(settings, progress, _inventoryCancellation.Token);
            RefreshInventoryView();
            if (_inventoryProgressText != null)
                _inventoryProgressText.Text = _lastInventoryResult.Success
                    ? LocalizationService.Get("inventory.completed", "Inventarisierung abgeschlossen.")
                    : LocalizationService.Get("inventory.failed", "Inventarisierung ohne verwertbare Gerätedaten beendet.");
        }
        catch (OperationCanceledException)
        {
            if (_inventoryProgressText != null) _inventoryProgressText.Text = LocalizationService.Get("inventory.cancelled", "Inventarisierung abgebrochen.");
        }
        catch (Exception ex)
        {
            if (_inventoryProgressText != null) _inventoryProgressText.Text = LocalizationService.Get("inventory.failed", "Inventarisierung fehlgeschlagen.");
            if (_inventorySummaryBox != null) _inventorySummaryBox.Text = ex.ToString();
        }
    }

    private void RefreshInventoryView()
    {
        if (_lastInventoryResult == null) return;
        var inventory = _lastInventoryResult.Inventory;
        if (_inventorySummaryBox != null)
            _inventorySummaryBox.Text = SshInventoryService.BuildSummary(
                inventory,
                _lastInventoryResult.Errors,
                LocalizationService.CurrentLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        if (_inventoryInterfaceGrid != null) _inventoryInterfaceGrid.ItemsSource = inventory.Interfaces;
        if (_inventoryVlanGrid != null) _inventoryVlanGrid.ItemsSource = inventory.Vlans;
        if (_inventoryNeighborGrid != null) _inventoryNeighborGrid.ItemsSource = inventory.Neighbors;
        if (_inventoryRawBox != null)
        {
            var sb = new StringBuilder();
            foreach (var (key, value) in inventory.RawOutputs.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"===== {key} =====");
                sb.AppendLine(value);
                sb.AppendLine();
            }
            _inventoryRawBox.Text = sb.ToString();
        }
    }

    private ProjectDeviceSnapshot? ApplyInventoryToProject(bool showMessage = true)
    {
        if (_lastInventoryResult is not { Success: true } result)
        {
            if (showMessage) MessageBox.Show(this, LocalizationService.Get("inventory.no_data", "Noch keine Inventarisierung durchgeführt."), LocalizationService.Get("inventory.title", "SSH-Live-Inventarisierung"), MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        var inventory = result.Inventory;
        var name = string.IsNullOrWhiteSpace(inventory.Hostname) ? inventory.SourceHost : inventory.Hostname;
        var device = _currentProject.Devices.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (device == null)
        {
            device = new ProjectDeviceSnapshot
            {
                Name = name,
                DeviceType = string.IsNullOrWhiteSpace(inventory.DeviceType) ? "Router" : inventory.DeviceType,
                ConfigMode = "Ohne VRF",
                Site = InferInventorySite(name),
                TopologyRole = "Automatisch",
                Modules = ModuleCatalog.All.ToDictionary(module => module.Name, _ => false, StringComparer.OrdinalIgnoreCase),
                Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                LastUpdatedUtc = DateTime.UtcNow
            };
            _currentProject.Devices.Add(device);
        }
        device.Inventory = inventory;
        device.Name = name;
        if (!string.IsNullOrWhiteSpace(inventory.DeviceType)) device.DeviceType = inventory.DeviceType;
        device.Values["hostname"] = name;
        device.Values["inventoryModel"] = inventory.Model;
        device.Values["inventorySerial"] = inventory.SerialNumber;
        device.Values["inventorySoftwareVersion"] = inventory.SoftwareVersion;
        device.LastUpdatedUtc = DateTime.UtcNow;
        RefreshProjectDeviceBindings();
        RefreshNetworkDiagram();
        ScheduleAutoSave();
        if (showMessage)
            MessageBox.Show(this,
                LocalizationService.Format("inventory.device_applied", name),
                LocalizationService.Get("inventory.title", "SSH-Live-Inventarisierung"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        return device;
    }

    private void ImportInventoryIntoIpam()
    {
        var device = ApplyInventoryToProject(showMessage: false);
        if (device == null || _lastInventoryResult == null) return;
        var config = _lastInventoryResult.Inventory.InterfaceConfiguration;
        if (string.IsNullOrWhiteSpace(config))
        {
            MessageBox.Show(this, LocalizationService.Get("inventory.no_interface_config", "Es wurde keine auswertbare Interface-Konfiguration erfasst."), LocalizationService.Get("inventory.title", "SSH-Live-Inventarisierung"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var imported = IpamService.ImportFromConfiguration(config, device.Name);
        var added = 0;
        foreach (var entry in imported)
        {
            var duplicate = _currentProject.IpamEntries.Any(existing =>
                existing.Network.Equals(entry.Network, StringComparison.OrdinalIgnoreCase) &&
                existing.PrefixLength == entry.PrefixLength &&
                existing.Device.Equals(entry.Device, StringComparison.OrdinalIgnoreCase) &&
                existing.Interface.Equals(entry.Interface, StringComparison.OrdinalIgnoreCase));
            if (duplicate) continue;
            _currentProject.IpamEntries.Add(entry);
            added++;
        }
        _ipamGrid?.Items.Refresh();
        ScheduleAutoSave();
        MessageBox.Show(this, LocalizationService.Format("inventory.ipam_added", added), LocalizationService.Get("inventory.title", "SSH-Live-Inventarisierung"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportInventoryNeighbors()
    {
        var device = ApplyInventoryToProject(showMessage: false);
        if (device == null || _lastInventoryResult == null) return;
        var outputs = _lastInventoryResult.Inventory.RawOutputs;
        var neighborText = string.Join("\n", new[]
        {
            outputs.TryGetValue("cdp", out var cdp) ? cdp : string.Empty,
            outputs.TryGetValue("lldp", out var lldp) ? lldp : string.Empty
        });
        var result = TopologyPlanningService.AddDiscoveredLinks(_currentProject, device, neighborText);
        RefreshNetworkDiagram();
        ScheduleAutoSave();
        var unresolved = result.UnresolvedDevices.Count == 0 ? "-" : string.Join(", ", result.UnresolvedDevices);
        MessageBox.Show(this,
            LocalizationService.Format("inventory.neighbors_added", result.AddedLinks, result.SkippedLinks, unresolved),
            LocalizationService.Get("inventory.title", "SSH-Live-Inventarisierung"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ExportInventoryJson()
    {
        if (_lastInventoryResult == null) return;
        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.Get("inventory.export", "Inventar exportieren"),
            Filter = "JSON (*.json)|*.json",
            FileName = $"{SanitizeFileName(_lastInventoryResult.Inventory.Hostname)}_inventory_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        var json = JsonSerializer.Serialize(_lastInventoryResult.Inventory, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dialog.FileName, json, new UTF8Encoding(false));
    }

    private static string InferInventorySite(string hostname)
    {
        var match = System.Text.RegularExpressions.Regex.Match(hostname ?? string.Empty, @"^([A-Za-z]{2,8})[-_]");
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : string.Empty;
    }
}
