using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CiscoConfigGuiWpf;

public partial class MainWindow
{
    private DataGrid? _pluginManagerGrid;
    private TextBox? _pluginDiagnosticsBox;

    private void BuildPluginManagerTab()
    {
        var tab = new TabItem
        {
            Header = LocalizationService.Get("plugins.manager_title", "Plugin-Manager")
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 10)
        };
        header.SetResourceReference(Border.BackgroundProperty, "PanelBg");
        header.SetResourceReference(Border.BorderBrushProperty, "BorderBrushSoft");

        var headerText = new StackPanel();
        headerText.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("plugins.manager_title", "Plugin-Manager"),
            FontSize = 22,
            FontWeight = FontWeights.Bold
        });
        headerText.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("plugins.manager_description", "Zeigt datenbasierte Plugins an, prüft deren Struktur und erlaubt das Aktivieren oder Deaktivieren. Änderungen an aktiven Modulen werden nach einem Neustart wirksam."),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Child = headerText;
        root.Children.Add(header);

        var managerContent = CreatePluginManagerContent();
        Grid.SetRow(managerContent, 1);
        root.Children.Add(managerContent);

        tab.Content = root;

        _tabsByName["Plugin-Manager"] = tab;
        MainTabs.Items.Add(tab);
        MainTabs.SelectionChanged += (_, eventArgs) =>
        {
            if (ReferenceEquals(eventArgs.OriginalSource, MainTabs) &&
                ReferenceEquals(MainTabs.SelectedItem, tab))
            {
                RefreshPluginManager(false);
            }
        };
    }

    private Grid CreatePluginManagerContent()
    {
        var root = new Grid { Margin = new Thickness(4, 0, 4, 8) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) });

        _pluginManagerGrid = new DataGrid
        {
            IsReadOnly = true,
            AutoGenerateColumns = false,
            SelectionMode = DataGridSelectionMode.Single,
            Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 56, 70))
        };
        _pluginManagerGrid.Columns.Add(new DataGridTextColumn
        {
            Header = LocalizationService.Get("plugins.name", "Name"),
            Binding = new Binding(nameof(PluginStatusInfo.Name)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        _pluginManagerGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "ID",
            Binding = new Binding(nameof(PluginStatusInfo.Id)),
            Width = 170
        });
        _pluginManagerGrid.Columns.Add(new DataGridTextColumn
        {
            Header = LocalizationService.Get("plugins.version", "Version"),
            Binding = new Binding(nameof(PluginStatusInfo.Version)),
            Width = 90
        });
        _pluginManagerGrid.Columns.Add(new DataGridTextColumn
        {
            Header = LocalizationService.Get("plugins.state", "Status"),
            Binding = new Binding(nameof(PluginStatusInfo.DisplayState)),
            Width = 95
        });
        _pluginManagerGrid.Columns.Add(new DataGridTextColumn
        {
            Header = LocalizationService.Get("plugins.modules", "Module"),
            Binding = new Binding(nameof(PluginStatusInfo.ModuleCount)),
            Width = 70
        });
        _pluginManagerGrid.Columns.Add(new DataGridTextColumn
        {
            Header = LocalizationService.Get("plugins.languages", "Sprachen"),
            Binding = new Binding(nameof(PluginStatusInfo.Languages)),
            Width = 135
        });
        _pluginManagerGrid.SelectionChanged += (_, _) => ShowSelectedPluginDiagnostics();
        root.Children.Add(_pluginManagerGrid);

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        var refresh = new Button { Content = LocalizationService.Get("plugins.refresh", "Neu prüfen") };
        var enable = new Button { Content = LocalizationService.Get("plugins.enable", "Aktivieren"), Style = TryFindResource("PrimaryButtonStyle") as Style };
        var disable = new Button { Content = LocalizationService.Get("plugins.disable", "Deaktivieren") };
        var openFolder = new Button { Content = LocalizationService.Get("plugins.open_folder", "Plugin-Ordner öffnen") };
        var restart = new Button { Content = LocalizationService.Get("plugins.restart", "Anwendung neu starten") };
        var validateAtStartup = new CheckBox
        {
            Content = LocalizationService.Get("plugins.validate_startup", "Plugins beim Anwendungsstart prüfen"),
            IsChecked = _appSettings.ValidatePluginsOnStartup,
            ToolTip = LocalizationService.Get("plugins.validate_startup_help", "Prüft Plugin-Dateien beim Start auf Format, Sprachvollständigkeit, Modulkonflikte und ungültige Generatoren."),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 3, 3, 3)
        };
        validateAtStartup.Checked += (_, _) => SavePluginStartupValidation(true);
        validateAtStartup.Unchecked += (_, _) => SavePluginStartupValidation(false);
        refresh.Click += (_, _) => RefreshPluginManager(true);
        enable.Click += (_, _) => ChangeSelectedPluginState(true);
        disable.Click += (_, _) => ChangeSelectedPluginState(false);
        openFolder.Click += (_, _) => OpenPluginDirectory();
        restart.Click += (_, _) => RestartApplicationForPlugins();
        foreach (var button in new[] { refresh, enable, disable, openFolder, restart })
        {
            button.Margin = new Thickness(3);
            actions.Children.Add(button);
        }
        actions.Children.Add(validateAtStartup);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);

        _pluginDiagnosticsBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 25)),
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 56, 70)),
            FontFamily = new FontFamily("Consolas"),
            Padding = new Thickness(10)
        };
        Grid.SetRow(_pluginDiagnosticsBox, 2);
        root.Children.Add(_pluginDiagnosticsBox);

        return root;
    }

    private void RefreshPluginManager(bool force)
    {
        if (_pluginManagerGrid == null) return;
        var statuses = PluginModuleService.GetPluginStatuses(force);
        _pluginManagerGrid.ItemsSource = statuses;
        _pluginManagerGrid.Items.Refresh();
        if (statuses.Count == 0 && _pluginDiagnosticsBox != null)
            _pluginDiagnosticsBox.Text = LocalizationService.Get("plugins.none", "Keine aktiven Plugin-Dateien (*.ciscoplugin.json) gefunden.");
    }

    private void ShowSelectedPluginDiagnostics()
    {
        if (_pluginDiagnosticsBox == null) return;
        if (_pluginManagerGrid?.SelectedItem is not PluginStatusInfo status)
        {
            _pluginDiagnosticsBox.Text = LocalizationService.Get("plugins.select", "Plugin auswählen, um die Diagnose anzuzeigen.");
            return;
        }
        _pluginDiagnosticsBox.Text = PluginModuleService.BuildDiagnostics(
            status,
            LocalizationService.CurrentLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase));
    }

    private void ChangeSelectedPluginState(bool enabled)
    {
        if (_pluginManagerGrid?.SelectedItem is not PluginStatusInfo status)
        {
            MessageBox.Show(this,
                LocalizationService.Get("plugins.select", "Plugin auswählen, um die Diagnose anzuzeigen."),
                LocalizationService.Get("plugins.manager_title", "Plugin-Manager"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var success = PluginModuleService.SetPluginEnabled(status.Id, enabled, out var technicalMessage);
        if (success)
            _appSettings = ApplicationSettingsService.Current;
        var message = success
            ? LocalizationService.Get("plugins.restart_required", "Die Plugin-Einstellung wurde gespeichert. Die Anwendung muss neu gestartet werden, damit sich die Moduloberfläche ändert.")
            : technicalMessage;
        MessageBox.Show(this,
            message,
            LocalizationService.Get("plugins.manager_title", "Plugin-Manager"),
            MessageBoxButton.OK,
            success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        RefreshPluginManager(true);
    }

    private void SavePluginStartupValidation(bool enabled)
    {
        _appSettings.ValidatePluginsOnStartup = enabled;
        ApplicationSettingsService.Save(_appSettings);
    }

    private void OpenPluginDirectory()
    {
        try
        {
            var directory = PluginModuleService.GetPluginDirectories().Last();
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.Get("plugins.manager_title", "Plugin-Manager"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RestartApplicationForPlugins()
    {
        if (MessageBox.Show(this,
                LocalizationService.Get("plugins.restart_confirm", "Anwendung jetzt neu starten? Nicht gespeicherte Änderungen gehen verloren."),
                LocalizationService.Get("plugins.restart", "Anwendung neu starten"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException(LocalizationService.Get("plugins.error_executable_path", "Der Pfad der ausführbaren Anwendung ist nicht verfügbar."));
            Process.Start(new ProcessStartInfo { FileName = executable, UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.Get("plugins.manager_title", "Plugin-Manager"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
