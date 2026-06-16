using Microsoft.Win32;
using System.Globalization;
using IOPath = System.IO.Path;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CiscoConfigGuiWpf;

public partial class MainWindow
{
    private static string T(string key) => LocalizationService.Get(key);

    private ApplicationSettings _appSettings = ApplicationSettingsService.Current;
    private readonly Dictionary<string, Control> _settingsControls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<object, OriginalUiText> _originalUiTexts = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<DataGrid> _localizedDataGrids = new(ReferenceEqualityComparer.Instance);
    private bool _applyingSettings;
    private bool _languagePreviewQueued;
    private bool _localizationTabHookInstalled;
    private string _lastAppliedLanguage = string.Empty;

    private sealed class OriginalUiText
    {
        public bool TextCaptured { get; set; }
        public string? Text { get; set; }
        public bool ContentCaptured { get; set; }
        public string? Content { get; set; }
        public bool HeaderCaptured { get; set; }
        public string? Header { get; set; }
        public bool ToolTipCaptured { get; set; }
        public string? ToolTip { get; set; }
        public bool BackgroundCaptured { get; set; }
        public Brush? Background { get; set; }
        public bool BorderCaptured { get; set; }
        public Brush? Border { get; set; }
        public bool ForegroundCaptured { get; set; }
        public Brush? Foreground { get; set; }
        public bool StrokeCaptured { get; set; }
        public Brush? Stroke { get; set; }
        public bool FillCaptured { get; set; }
        public Brush? Fill { get; set; }
    }

    private void InitializeApplicationSettings()
    {
        _appSettings = ApplicationSettingsService.Load();
        LocalizationService.SetLanguage(_appSettings.Language);
        ApplyThemeResources();
    }

    private void BuildSettingsTab()
    {
        var tab = new TabItem { Header = "⚙  " + T("navigation.settings") };
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
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerText = new StackPanel();
        headerText.Children.Add(new TextBlock { Text = T("settings.title"), FontSize = 22, FontWeight = FontWeights.Bold });
        headerText.Children.Add(new TextBlock
        {
            Text = T("settings.subtitle"),
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        headerGrid.Children.Add(headerText);

        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var apply = new Button { Content = T("common.apply"), Style = TryFindResource("PrimaryButtonStyle") as Style };
        var export = new Button { Content = T("settings.export") };
        var import = new Button { Content = T("settings.import") };
        var defaults = new Button { Content = T("settings.defaults") };
        apply.Click += (_, _) => ApplySettingsFromPage(true);
        export.Click += (_, _) => ExportApplicationSettings();
        import.Click += (_, _) => ImportApplicationSettings();
        defaults.Click += (_, _) => ResetApplicationSettings();
        actions.Children.Add(apply);
        actions.Children.Add(export);
        actions.Children.Add(import);
        actions.Children.Add(defaults);
        Grid.SetColumn(actions, 1);
        headerGrid.Children.Add(actions);
        header.Child = headerGrid;
        root.Children.Add(header);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 10) };

        stack.Children.Add(CreateSettingsCategory(T("settings.category.general"), T("text.sprache_startverhalten_und_grundlegende_sicherheitsabfragen"), panel =>
        {
            AddSettingsCombo(panel, "language", T("settings.application_language"), new[] { "Systemsprache", "Deutsch", "English" }, LanguageToDisplay(_appSettings.Language));
            AddSettingsCombo(panel, "reportLanguage", T("settings.report_language"), new[] { "Systemsprache", "Deutsch", "English" }, LanguageToDisplay(_appSettings.ReportLanguage));
            AddSettingsCombo(panel, "startPage", T("settings.start_page"), new[] { "Übersicht", "Basis", "Projekt", "Ausgabe", "Diagramm / Bericht" }, _appSettings.StartPage);
            AddSettingsCheck(panel, "loadLastProject", T("text.letztes_projekt_automatisch_laden"), _appSettings.LoadLastProject);
            AddSettingsCheck(panel, "confirmReset", T("text.zurucksetzen_bestatigen"), _appSettings.ConfirmReset);
        }, true));

        stack.Children.Add(CreateSettingsCategory(T("settings.category.appearance"), T("text.design_und_informationsdichte_der_benutzeroberflache"), panel =>
        {
            AddSettingsCombo(panel, "theme", T("settings.theme"), new[] { "Dunkel", "Hell", "System" }, _appSettings.Theme);
            AddSettingsCombo(panel, "accentColor", T("settings.accent_color"), new[] { "Orange", "Blau", "Grün", "Violett" }, _appSettings.AccentColor);
            AddSettingsCombo(panel, "fontSize", T("settings.font_size"), new[] { "Klein", "Normal", "Groß" }, _appSettings.FontSize);

            var resetAccent = new Button
            {
                Content = T("settings.reset_accent"),
                ToolTip = T("text.setzt_die_akzentfarbe_auf_orange_zuruck"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            resetAccent.Click += (_, _) => ResetAccentColorPreview();
            panel.Children.Add(CreateSettingsCard(string.Empty, resetAccent));
            AddSettingsCheck(panel, "compactMode", T("text.kompakte_ansicht"), _appSettings.CompactMode);
            AddSettingsCheck(panel, "animations", T("text.animationen_aktivieren"), _appSettings.AnimationsEnabled);
            AddSettingsCheck(panel, "tooltips", T("text.tooltips_aktivieren"), _appSettings.TooltipsEnabled);
            AddSettingsCheck(panel, "livePreview", T("text.live_vorschau_anzeigen"), _appSettings.LivePreviewEnabled);
            AddSettingsCheck(panel, "collapsibleNavigation", T("text.navigation_einklappbar"), _appSettings.CollapsibleNavigation);
        }));

        stack.Children.Add(CreateSettingsCategory(T("settings.category.configuration"), T("text.standardwerte_und_aufbau_der_erzeugten_cisco_konfiguration"), panel =>
        {
            AddSettingsCombo(panel, "defaultDevice", T("text.standardgerat"), new[] { "Router", "L3-Switch", "L2-Switch" }, _appSettings.DefaultDeviceType);
            AddSettingsCombo(panel, "defaultMode", T("text.standardmodus"), new[] { "Ohne VRF", "Mit VRF" }, _appSettings.DefaultConfigMode);
            AddSettingsCombo(panel, "defaultPlatform", T("text.standardplattform"), new[] { "IOS-XE", "IOS", "Packet Tracer", "CML / EVE-NG" }, _appSettings.DefaultPlatform);
            AddSettingsText(panel, "defaultInterface", T("text.standard_interface"), _appSettings.DefaultInterfacePattern);
            AddSettingsCombo(panel, "defaultIpStack", T("text.standard_ip_stack"), new[] { "Dual Stack", "Nur IPv4", "Nur IPv6" }, _appSettings.DefaultIpStack);
            AddSettingsCheck(panel, "includeComments", T("text.kommentare_erzeugen"), _appSettings.IncludeComments);
            AddSettingsCheck(panel, "includeSeparators", T("text.abschnittstrenner_erzeugen"), _appSettings.IncludeSectionSeparators);
            AddSettingsCheck(panel, "includeEnable", T("text.enable_exportieren"), _appSettings.IncludeEnable);
            AddSettingsCheck(panel, "includeConfigureTerminal", T("text.configure_terminal_exportieren"), _appSettings.IncludeConfigureTerminal);
            AddSettingsCheck(panel, "includeEnd", T("text.end_exportieren"), _appSettings.IncludeEnd);
            AddSettingsCheck(panel, "includeWriteMemory", T("text.write_memory_exportieren"), _appSettings.IncludeWriteMemory);
            AddSettingsCombo(panel, "validationMode", T("text.validierungsmodus"), new[] { "Streng", "Tolerant" }, _appSettings.ValidationMode);
            AddSettingsCombo(panel, "autoFixMode", T("text.automatische_korrekturen"), new[] { "Nur vorschlagen", "Automatisch anwenden" }, _appSettings.AutoFixMode);
        }));

        stack.Children.Add(CreateSettingsCategory(T("settings.category.import_export"), T("text.dateinamen_zielordner_und_zusatzliche_exportartefakte"), panel =>
        {
            AddSettingsText(panel, "exportFolder", T("text.standard_exportordner"), _appSettings.DefaultExportFolder);
            AddSettingsText(panel, "fileNamePattern", T("text.dateinamensmuster"), _appSettings.ExportFileNamePattern);
            AddSettingsCombo(panel, "lineEndings", T("text.zeilenenden"), new[] { "Windows (CRLF)", "Unix (LF)" }, _appSettings.LineEndings);
            AddSettingsCheck(panel, "sortByModules", T("text.nach_modulen_sortieren"), _appSettings.SortConfigurationByModules);
            AddSettingsCheck(panel, "keepUnknown", T("text.unbekannte_befehle_ubernehmen"), _appSettings.KeepUnknownCommands);
            AddSettingsCheck(panel, "includeCustom", T("text.zusatzbefehle_ubernehmen"), _appSettings.IncludeCustomCommands);
            AddSettingsCheck(panel, "timestampFile", T("text.zeitstempel_im_dateinamen"), _appSettings.TimestampInFileName);
            AddSettingsCheck(panel, "exportPeer", T("text.gegenstellenkonfiguration_mitexportieren"), _appSettings.ExportPeerConfiguration);
            AddSettingsCheck(panel, "generateRollback", T("text.rollback_datei_erzeugen"), _appSettings.GenerateRollbackFile);
            AddSettingsCheck(panel, "exportReports", T("text.berichte_gemeinsam_exportieren"), _appSettings.ExportReportsTogether);
        }));

        stack.Children.Add(CreateSettingsCategory(T("settings.category.projects_backup"), T("text.automatische_sicherung_wiederherstellung_und_historie"), panel =>
        {
            AddSettingsCheck(panel, "autoSave", T("text.autosave_aktivieren"), _appSettings.AutoSaveEnabled);
            AddSettingsText(panel, "autoSaveInterval", T("text.autosave_intervall_sekunden"), _appSettings.AutoSaveIntervalSeconds.ToString(CultureInfo.InvariantCulture));
            AddSettingsText(panel, "backupCount", T("text.anzahl_lokaler_sicherungen"), _appSettings.BackupCount.ToString(CultureInfo.InvariantCulture));
            AddSettingsText(panel, "backupFolder", T("text.backup_ordner"), _appSettings.BackupFolder);
            AddSettingsCheck(panel, "saveOnExit", T("text.projekt_beim_beenden_speichern"), _appSettings.SaveProjectOnExit);
            AddSettingsCheck(panel, "restoreCrash", T("text.nach_absturz_wiederherstellen"), _appSettings.RestoreAfterCrash);
            AddSettingsCheck(panel, "history", T("text.konfigurationshistorie_aktivieren"), _appSettings.HistoryEnabled);
            AddSettingsText(panel, "historyLimit", T("text.maximale_historieneintrage"), _appSettings.HistoryLimit.ToString(CultureInfo.InvariantCulture));
        }));

        stack.Children.Add(CreateSettingsCategory(T("settings.category.ssh_operations"), T("text.standardwerte_fur_geratezugriff_ubertragung_und_backups"), panel =>
        {
            AddSettingsText(panel, "sshPort", T("text.standard_ssh_port"), _appSettings.DefaultSshPort.ToString(CultureInfo.InvariantCulture));
            AddSettingsText(panel, "connectionTimeout", T("text.verbindungs_timeout_sekunden"), _appSettings.ConnectionTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            AddSettingsText(panel, "commandTimeout", T("text.befehls_timeout_sekunden"), _appSettings.CommandTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            AddSettingsText(panel, "commandDelay", T("text.zeilenverzogerung_ms"), _appSettings.CommandDelayMilliseconds.ToString(CultureInfo.InvariantCulture));
            AddSettingsCheck(panel, "backupBeforeTransfer", T("text.vor_ubertragung_sichern"), _appSettings.BackupBeforeTransfer);
            AddSettingsCheck(panel, "abortOnError", T("text.bei_fehler_abbrechen"), _appSettings.AbortTransferOnError);
            AddSettingsCheck(panel, "showBeforeSend", T("text.befehle_vor_dem_senden_anzeigen"), _appSettings.ShowCommandsBeforeSend);
            AddSettingsCheck(panel, "storePasswords", T("text.passworter_speichern"), _appSettings.StorePasswords,
                T("text.passworter_werden_standardma_ig_nicht_gespeichert_eine_aktiv"));
            AddSettingsCheck(panel, "sessionLogging", T("text.sitzungsprotokoll_aktivieren"), _appSettings.SessionLoggingEnabled);
            AddSettingsText(panel, "deviceBackupFolder", T("text.gerate_backup_ordner"), _appSettings.DeviceBackupFolder);
        }));

        stack.Children.Add(CreateSettingsCategory(T("settings.category.diagram_report"), T("text.darstellung_der_topologie_und_vorgaben_fur_projektberichte"), panel =>
        {
            AddSettingsCheck(panel, "autoLayout", T("text.automatische_diagrammanordnung"), _appSettings.AutomaticDiagramLayout);
            AddSettingsCheck(panel, "snapGrid", T("text.am_raster_ausrichten"), _appSettings.SnapDiagramToGrid);
            AddSettingsCheck(panel, "showLinkTypes", T("text.verbindungstypen_anzeigen"), _appSettings.ShowConnectionTypes);
            AddSettingsCheck(panel, "showInterfaces", T("text.interface_namen_anzeigen"), _appSettings.ShowInterfaceNames);
            AddSettingsCheck(panel, "showIps", T("text.ip_adressen_anzeigen"), _appSettings.ShowIpAddresses);
            AddSettingsCheck(panel, "showVlans", T("text.vlans_anzeigen"), _appSettings.ShowVlans);
            AddSettingsCombo(panel, "reportFormat", T("text.standard_berichtsformat"), new[] { "PDF", "DOCX", "HTML" }, _appSettings.DefaultReportFormat);
            AddSettingsText(panel, "company", T("text.firmenname"), _appSettings.CompanyName);
            AddSettingsText(panel, "projectManager", T("text.projektverantwortlicher"), _appSettings.ProjectManager);
            AddSettingsText(panel, "reportLogo", T("text.berichtslogo"), _appSettings.ReportLogoPath);
            AddSettingsCombo(panel, "pageSize", T("text.seitenformat"), new[] { "A4", "Letter" }, _appSettings.PageSize);
            AddSettingsCombo(panel, "pageOrientation", T("text.ausrichtung"), new[] { "Hochformat", "Querformat" }, _appSettings.PageOrientation);
        }));

        stack.Children.Add(CreateSettingsCategory(T("settings.category.advanced"), T("text.zusatzliche_prufungen_und_technische_diagnoseoptionen"), panel =>
        {
            AddSettingsCheck(panel, "strictImport", T("text.strenge_importprufung"), _appSettings.StrictImportValidation,
                T("text.die_strenge_importprufung_meldet_unvollstandige_widerspruchl"));
            AddSettingsCheck(panel, "developerMode", T("settings.developer_mode"), _appSettings.DeveloperMode,
                T("text.entwicklermodus_zeigt_interne_modul_und_feld_ids_in_tooltips"));

            var developerInfo = new Border
            {
                Width = 708,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(4),
                Child = new TextBlock
                {
                    Text = T("text.der_entwicklermodus_ist_ausschlie_lich_fur_fehlersuche_und_u"),
                    TextWrapping = TextWrapping.Wrap
                }
            };
            developerInfo.SetResourceReference(Border.BackgroundProperty, "CardBg");
            developerInfo.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            panel.Children.Add(developerInfo);

            AddSettingsCheck(panel, "diagnosticDetails", T("text.diagnosedetails_aufnehmen"), _appSettings.IncludeDiagnosticDetails,
                T("text.diagnosedetails_erganzen_fehlermeldungen_und_protokolle_um_t"));

            var auditTranslations = new Button { Content = T("settings.audit_translation"), HorizontalAlignment = HorizontalAlignment.Stretch };
            auditTranslations.ToolTip = TooltipBuilder.Create(T("settings.audit_translation"), T("text.durchsucht_die_aktuell_sichtbare_oberflache_einschlie_lich_t"));
            auditTranslations.Click += (_, _) => RunTranslationAudit();
            panel.Children.Add(CreateSettingsCard(string.Empty, auditTranslations));

            var openDiagnostics = new Button { Content = T("settings.open_diagnostics"), HorizontalAlignment = HorizontalAlignment.Stretch };
            openDiagnostics.ToolTip = TooltipBuilder.Create(T("settings.open_diagnostics"), T("text.offnet_den_ordner_mit_entwicklerprotokoll_ubersetzungsprufun"));
            openDiagnostics.Click += (_, _) => DeveloperDiagnosticsService.OpenLogDirectory();
            panel.Children.Add(CreateSettingsCard(string.Empty, openDiagnostics));
        }));

        var note = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(44, 26, 12)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(120, 65, 18)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 6, 0, 0),
            Child = new TextBlock
            {
                Text = T("settings.language_note"),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(253, 186, 116))
            }
        };
        stack.Children.Add(note);
        scroll.Content = stack;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        tab.Content = root;
        _tabsByName["Einstellungen"] = tab;
        MainTabs.Items.Add(tab);

        if (!_localizationTabHookInstalled)
        {
            _localizationTabHookInstalled = true;
            MainTabs.SelectionChanged += (_, e) =>
            {
                if (!ReferenceEquals(e.Source, MainTabs)) return;
                if (MainTabs.SelectedContent is DependencyObject selectedContent)
                    LocalizeObjectTree(selectedContent);
            };
        }

        if (_settingsControls.TryGetValue("language", out var languageControl) && languageControl is ComboBox languageCombo)
        {
            languageCombo.SelectionChanged += (_, _) =>
            {
                if (_applyingSettings) return;
                var selected = languageCombo.SelectedItem?.ToString() ?? "Systemsprache";
                QueueLanguagePreview(DisplayToLanguage(selected));
            };
        }

        if (_settingsControls.TryGetValue("theme", out var themeControl) && themeControl is ComboBox themeCombo)
        {
            themeCombo.SelectionChanged += (_, _) =>
            {
                if (_applyingSettings) return;
                _appSettings.Theme = themeCombo.SelectedItem?.ToString() ?? "Dunkel";
                ApplyThemeResources();
                ApplyThemeToVisualTree();
            };
        }

        if (_settingsControls.TryGetValue("accentColor", out var accentControl) && accentControl is ComboBox accentCombo)
        {
            accentCombo.SelectionChanged += (_, _) =>
            {
                if (_applyingSettings) return;
                _appSettings.AccentColor = accentCombo.SelectedItem?.ToString() ?? "Orange";
                ApplyThemeResources();
                ApplyThemeToVisualTree();
            };
        }
    }
    private void QueueLanguagePreview(string language)
    {
        LocalizationService.SetLanguage(language);
        if (_languagePreviewQueued) return;

        _languagePreviewQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _languagePreviewQueued = false;
            if (string.Equals(_lastAppliedLanguage, LocalizationService.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
                return;

            var previousCursor = Cursor;
            try
            {
                Cursor = System.Windows.Input.Cursors.Wait;
                ApplyLocalizationToVisualTree();
                _lastAppliedLanguage = LocalizationService.CurrentLanguage;
                RefreshNetworkDiagram();
                UpdateStatusBar();
            }
            finally
            {
                Cursor = previousCursor;
            }
        }));
    }

    private void ResetAccentColorPreview()
    {
        _appSettings.AccentColor = "Orange";
        _applyingSettings = true;
        try
        {
            SetSettingCombo("accentColor", "Orange");
        }
        finally
        {
            _applyingSettings = false;
        }
        ApplyThemeResources();
        ApplyThemeToVisualTree();
    }

    private Expander CreateSettingsCategory(string title, string subtitle, Action<WrapPanel> build, bool expanded = false)
    {
        var panel = new WrapPanel { Margin = new Thickness(2, 8, 2, 4) };
        build(panel);
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold });
        header.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });
        var expander = new Expander
        {
            Header = header,
            Content = panel,
            IsExpanded = expanded,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8)
        };
        expander.SetResourceReference(Control.BackgroundProperty, "PanelBg");
        expander.SetResourceReference(Control.BorderBrushProperty, "BorderBrushSoft");
        return expander;
    }

    private void AddSettingsCombo(Panel panel, string key, string label, IEnumerable<string> options, string selected)
    {
        var combo = new ComboBox { ItemsSource = options.ToArray(), MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.SelectedItem = options.FirstOrDefault(x => x.Equals(selected, StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();
        combo.ItemTemplate = LocalizationService.CreateLocalizedStringTemplate();
        _settingsControls[key] = combo;
        panel.Children.Add(CreateSettingsCard(label, combo));
    }

    private void AddSettingsText(Panel panel, string key, string label, string value)
    {
        var box = new TextBox { Text = value ?? string.Empty, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch };
        _settingsControls[key] = box;
        panel.Children.Add(CreateSettingsCard(label, box));
    }

    private void AddSettingsCheck(Panel panel, string key, string label, bool value, string? tooltip = null)
    {
        var check = new CheckBox { Content = label, IsChecked = value, VerticalAlignment = VerticalAlignment.Center, ToolTip = tooltip };
        _settingsControls[key] = check;
        panel.Children.Add(CreateSettingsCard(string.Empty, check));
    }

    private Border CreateSettingsCard(string label, Control control)
    {
        var stack = new StackPanel();
        if (!string.IsNullOrWhiteSpace(label))
            stack.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        stack.Children.Add(control);
        var card = new Border
        {
            Width = 350,
            MinHeight = string.IsNullOrWhiteSpace(label) ? 76 : 96,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(11),
            Margin = new Thickness(4),
            Child = stack
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBg");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrushSoft");
        return card;
    }

    private void ApplySettingsFromPage(bool showConfirmation)
    {
        if (_applyingSettings) return;
        _applyingSettings = true;
        try
        {
            _appSettings.Language = DisplayToLanguage(GetSettingCombo("language", "Systemsprache"));
            _appSettings.ReportLanguage = DisplayToLanguage(GetSettingCombo("reportLanguage", "Systemsprache"));
            _appSettings.StartPage = GetSettingCombo("startPage", "Übersicht");
            _appSettings.LoadLastProject = GetSettingCheck("loadLastProject", false);
            _appSettings.ConfirmReset = GetSettingCheck("confirmReset", true);

            _appSettings.Theme = GetSettingCombo("theme", "Dunkel");
            _appSettings.AccentColor = GetSettingCombo("accentColor", "Orange");
            _appSettings.FontSize = GetSettingCombo("fontSize", "Normal");
            _appSettings.CompactMode = GetSettingCheck("compactMode", false);
            _appSettings.AnimationsEnabled = GetSettingCheck("animations", true);
            _appSettings.TooltipsEnabled = GetSettingCheck("tooltips", true);
            _appSettings.LivePreviewEnabled = GetSettingCheck("livePreview", true);
            _appSettings.CollapsibleNavigation = GetSettingCheck("collapsibleNavigation", true);

            _appSettings.DefaultDeviceType = GetSettingCombo("defaultDevice", "Router");
            _appSettings.DefaultConfigMode = GetSettingCombo("defaultMode", "Ohne VRF");
            _appSettings.DefaultPlatform = GetSettingCombo("defaultPlatform", "IOS-XE");
            _appSettings.DefaultInterfacePattern = GetSettingText("defaultInterface", "GigabitEthernet1/0/1");
            _appSettings.DefaultIpStack = GetSettingCombo("defaultIpStack", "Dual Stack");
            _appSettings.IncludeComments = GetSettingCheck("includeComments", true);
            _appSettings.IncludeSectionSeparators = GetSettingCheck("includeSeparators", true);
            _appSettings.IncludeEnable = GetSettingCheck("includeEnable", true);
            _appSettings.IncludeConfigureTerminal = GetSettingCheck("includeConfigureTerminal", true);
            _appSettings.IncludeEnd = GetSettingCheck("includeEnd", true);
            _appSettings.IncludeWriteMemory = GetSettingCheck("includeWriteMemory", true);
            _appSettings.ValidationMode = GetSettingCombo("validationMode", "Streng");
            _appSettings.AutoFixMode = GetSettingCombo("autoFixMode", "Nur vorschlagen");

            _appSettings.DefaultExportFolder = GetSettingText("exportFolder", "");
            _appSettings.ExportFileNamePattern = GetSettingText("fileNamePattern", "cisco_config_{hostname}");
            _appSettings.LineEndings = GetSettingCombo("lineEndings", "Windows (CRLF)");
            _appSettings.SortConfigurationByModules = GetSettingCheck("sortByModules", true);
            _appSettings.KeepUnknownCommands = GetSettingCheck("keepUnknown", true);
            _appSettings.IncludeCustomCommands = GetSettingCheck("includeCustom", true);
            _appSettings.TimestampInFileName = GetSettingCheck("timestampFile", false);
            _appSettings.ExportPeerConfiguration = GetSettingCheck("exportPeer", false);
            _appSettings.GenerateRollbackFile = GetSettingCheck("generateRollback", false);
            _appSettings.ExportReportsTogether = GetSettingCheck("exportReports", false);

            _appSettings.AutoSaveEnabled = GetSettingCheck("autoSave", true);
            _appSettings.AutoSaveIntervalSeconds = GetSettingInt("autoSaveInterval", 60, 10, 3600);
            _appSettings.BackupCount = GetSettingInt("backupCount", 20, 1, 500);
            _appSettings.BackupFolder = GetSettingText("backupFolder", "");
            _appSettings.SaveProjectOnExit = GetSettingCheck("saveOnExit", true);
            _appSettings.RestoreAfterCrash = GetSettingCheck("restoreCrash", true);
            _appSettings.HistoryEnabled = GetSettingCheck("history", true);
            _appSettings.HistoryLimit = GetSettingInt("historyLimit", 50, 1, 1000);

            _appSettings.DefaultSshPort = GetSettingInt("sshPort", 22, 1, 65535);
            _appSettings.ConnectionTimeoutSeconds = GetSettingInt("connectionTimeout", 15, 1, 600);
            _appSettings.CommandTimeoutSeconds = GetSettingInt("commandTimeout", 180, 10, 3600);
            _appSettings.CommandDelayMilliseconds = GetSettingInt("commandDelay", 45, 0, 5000);
            _appSettings.BackupBeforeTransfer = GetSettingCheck("backupBeforeTransfer", true);
            _appSettings.AbortTransferOnError = GetSettingCheck("abortOnError", true);
            _appSettings.ShowCommandsBeforeSend = GetSettingCheck("showBeforeSend", true);
            _appSettings.StorePasswords = GetSettingCheck("storePasswords", false);
            _appSettings.SessionLoggingEnabled = GetSettingCheck("sessionLogging", true);
            _appSettings.DeviceBackupFolder = GetSettingText("deviceBackupFolder", "");

            _appSettings.AutomaticDiagramLayout = GetSettingCheck("autoLayout", true);
            _appSettings.SnapDiagramToGrid = GetSettingCheck("snapGrid", true);
            _appSettings.ShowConnectionTypes = GetSettingCheck("showLinkTypes", true);
            _appSettings.ShowInterfaceNames = GetSettingCheck("showInterfaces", true);
            _appSettings.ShowIpAddresses = GetSettingCheck("showIps", false);
            _appSettings.ShowVlans = GetSettingCheck("showVlans", false);
            _appSettings.DefaultReportFormat = GetSettingCombo("reportFormat", "PDF");
            _appSettings.CompanyName = GetSettingText("company", "");
            _appSettings.ProjectManager = GetSettingText("projectManager", "");
            _appSettings.ReportLogoPath = GetSettingText("reportLogo", "");
            _appSettings.PageSize = GetSettingCombo("pageSize", "A4");
            _appSettings.PageOrientation = GetSettingCombo("pageOrientation", "Hochformat");

            _appSettings.StrictImportValidation = GetSettingCheck("strictImport", true);
            _appSettings.DeveloperMode = GetSettingCheck("developerMode", false);
            _appSettings.IncludeDiagnosticDetails = GetSettingCheck("diagnosticDetails", true);

            ApplicationSettingsService.Save(_appSettings);
            LocalizationService.SetLanguage(_appSettings.Language);
            ApplyRuntimeSettings();
            if (showConfirmation)
                MessageBox.Show(this, LocalizationService.Get("text.einstellungen_wurden_gespeichert"), LocalizationService.Get("navigation.settings"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    private void ApplyRuntimeSettings()
    {
        ApplyThemeResources();
        ApplyThemeToVisualTree();
        ApplyDisplayDensity();
        ApplyToolTipSetting();
        ApplyLivePreviewSetting();

        if (!string.Equals(_lastAppliedLanguage, LocalizationService.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            ApplyLocalizationToVisualTree();
            _lastAppliedLanguage = LocalizationService.CurrentLanguage;
        }

        DeveloperDiagnosticsService.Log("SETTINGS", $"Runtime settings applied. Language={LocalizationService.CurrentLanguage}, Theme={_appSettings.Theme}, Accent={_appSettings.AccentColor}");

        if (WriteMemCombo.Items.Count > 0)
            WriteMemCombo.SelectedItem = _appSettings.IncludeWriteMemory ? "Ja" : "Nein";
        if (_autoSaveTimer != null)
        {
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_appSettings.AutoSaveIntervalSeconds, 10, 3600));
            if (_appSettings.AutoSaveEnabled) _autoSaveTimer.Start(); else _autoSaveTimer.Stop();
        }
        if (_sshPortBox != null) _sshPortBox.Text = _appSettings.DefaultSshPort.ToString(CultureInfo.InvariantCulture);
        if (_sshDelayBox != null) _sshDelayBox.Text = _appSettings.CommandDelayMilliseconds.ToString(CultureInfo.InvariantCulture);
        RefreshNetworkDiagram();
        UpdateStatusBar();
    }

    private void ApplyDisplayDensity()
    {
        if (RootLayout == null) return;
        RootLayout.Margin = _appSettings.CompactMode ? new Thickness(8) : new Thickness(16);
        MainTabs.Margin = _appSettings.CompactMode ? new Thickness(0, 7, 0, 0) : new Thickness(0, 12, 0, 0);
        var size = _appSettings.FontSize switch { "Klein" => 12d, "Groß" => 15d, _ => 13d };
        RootLayout.SetValue(TextElement.FontSizeProperty, size);
    }

    private void ApplyToolTipSetting()
    {
        if (RootLayout != null)
            ToolTipService.SetIsEnabled(RootLayout, _appSettings.TooltipsEnabled);
    }

    private void ApplyLivePreviewSetting()
    {
        var visibility = _appSettings.LivePreviewEnabled ? Visibility.Visible : Visibility.Collapsed;
        foreach (var preview in _moduleLivePreviewBoxes.Values) preview.Visibility = visibility;
        foreach (var pair in _fieldDefinitions.Where(x => x.Value.Type.Equals("Preview", StringComparison.OrdinalIgnoreCase)))
            if (_fieldWrappers.TryGetValue(pair.Key, out var wrapper)) wrapper.Visibility = visibility;
    }

    private void ApplyThemeResources()
    {
        var actualTheme = ResolveTheme(_appSettings.Theme);
        var light = actualTheme.Equals("Hell", StringComparison.OrdinalIgnoreCase);
        var accent = GetAccentColor(_appSettings.AccentColor);
        SetBrushResource("AccentBrush", accent);
        SetBrushResource("AccentBrightBrush", Lighten(accent, 35));
        SetBrushResource("AccentHoverBrush", Lighten(accent, 18));
        SetBrushResource("AccentPressedBrush", Darken(accent, 38));
        SetBrushResource("AccentSoftBrush", Color.FromArgb(light ? (byte)42 : (byte)64, accent.R, accent.G, accent.B));
        SetBrushResource("CheckMarkBrush", Colors.White);

        if (light)
        {
            SetBrushResource("WindowBg", Color.FromRgb(241, 245, 249));
            SetBrushResource("NavigationBg", Color.FromRgb(255, 255, 255));
            SetBrushResource("PanelBg", Color.FromRgb(248, 250, 252));
            SetBrushResource("CardBg", Color.FromRgb(255, 255, 255));
            SetBrushResource("CardBgHover", Color.FromRgb(241, 245, 249));
            SetBrushResource("InputBg", Color.FromRgb(255, 255, 255));
            SetBrushResource("BorderBrushSoft", Color.FromRgb(203, 213, 225));
            SetBrushResource("BorderBrushStrong", Color.FromRgb(148, 163, 184));
            SetBrushResource("TextBrush", Color.FromRgb(15, 23, 42));
            SetBrushResource("MutedBrush", Color.FromRgb(71, 85, 105));
            Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        }
        else
        {
            SetBrushResource("WindowBg", Color.FromRgb(11, 14, 19));
            SetBrushResource("NavigationBg", Color.FromRgb(15, 18, 24));
            SetBrushResource("PanelBg", Color.FromRgb(18, 22, 30));
            SetBrushResource("CardBg", Color.FromRgb(23, 28, 37));
            SetBrushResource("CardBgHover", Color.FromRgb(27, 33, 44));
            SetBrushResource("InputBg", Color.FromRgb(14, 18, 25));
            SetBrushResource("BorderBrushSoft", Color.FromRgb(39, 46, 58));
            SetBrushResource("BorderBrushStrong", Color.FromRgb(56, 66, 82));
            SetBrushResource("TextBrush", Color.FromRgb(242, 244, 247));
            SetBrushResource("MutedBrush", Color.FromRgb(156, 166, 181));
            Background = new LinearGradientBrush(Color.FromRgb(9, 11, 15), Color.FromRgb(17, 21, 29), 45);
        }
    }

    private void ApplyThemeToVisualTree()
    {
        if (RootLayout == null) return;
        var light = ResolveTheme(_appSettings.Theme).Equals("Hell", StringComparison.OrdinalIgnoreCase);
        var accent = GetAccentColor(_appSettings.AccentColor);
        foreach (var element in EnumerateUiObjects(RootLayout))
        {
            // Elemente innerhalb eines ControlTemplates werden über DynamicResource und
            // TemplateBinding gestaltet. Lokale Farbwerte würden die Templates nach einem
            // Themewechsel überdecken und beispielsweise ComboBox-Inhalte unsichtbar machen.
            if (element is FrameworkElement templateElement && templateElement.TemplatedParent != null)
                continue;

            var original = GetOrCreateOriginal(element);
            if (element is Border border)
            {
                if (!UsesResourceExpression(border, Border.BackgroundProperty))
                {
                    CaptureBackground(original, border.Background);
                    border.Background = AdaptBackground(CloneBrush(original.Background), light);
                }
                if (!UsesResourceExpression(border, Border.BorderBrushProperty))
                {
                    CaptureBorder(original, border.BorderBrush);
                    border.BorderBrush = AdaptBorder(CloneBrush(original.Border), light, accent);
                }
            }
            else if (element is Panel panel)
            {
                if (!UsesResourceExpression(panel, Panel.BackgroundProperty))
                {
                    CaptureBackground(original, panel.Background);
                    panel.Background = AdaptBackground(CloneBrush(original.Background), light);
                }
            }
            else if (element is Control control)
            {
                if (!UsesResourceExpression(control, Control.BackgroundProperty))
                {
                    CaptureBackground(original, control.Background);
                    control.Background = AdaptBackground(CloneBrush(original.Background), light);
                }
                if (!UsesResourceExpression(control, Control.BorderBrushProperty))
                {
                    CaptureBorder(original, control.BorderBrush);
                    control.BorderBrush = AdaptBorder(CloneBrush(original.Border), light, accent);
                }
                if (!UsesResourceExpression(control, Control.ForegroundProperty))
                {
                    CaptureForeground(original, control.Foreground);
                    control.Foreground = AdaptForeground(CloneBrush(original.Foreground), light, accent);
                }
            }
            else if (element is TextBlock text)
            {
                if (!UsesResourceExpression(text, TextBlock.ForegroundProperty))
                {
                    CaptureForeground(original, text.Foreground);
                    text.Foreground = AdaptForeground(CloneBrush(original.Foreground), light, accent);
                }
            }
            else if (element is Shape shape)
            {
                if (!UsesResourceExpression(shape, Shape.StrokeProperty))
                {
                    if (!original.StrokeCaptured)
                    {
                        original.Stroke = CloneBrush(shape.Stroke);
                        original.StrokeCaptured = true;
                    }
                    shape.Stroke = AdaptBorder(CloneBrush(original.Stroke), light, accent);
                }
                if (!UsesResourceExpression(shape, Shape.FillProperty))
                {
                    if (!original.FillCaptured)
                    {
                        original.Fill = CloneBrush(shape.Fill);
                        original.FillCaptured = true;
                    }
                    shape.Fill = AdaptForeground(CloneBrush(original.Fill), light, accent);
                }
            }
        }
    }

    private static bool UsesResourceExpression(DependencyObject owner, DependencyProperty property)
    {
        try
        {
            return DependencyPropertyHelper.GetValueSource(owner, property).IsExpression;
        }
        catch
        {
            return false;
        }
    }

    private static void CaptureBackground(OriginalUiText original, Brush? brush)
    {
        if (original.BackgroundCaptured) return;
        original.Background = CloneBrush(brush);
        original.BackgroundCaptured = true;
    }

    private static void CaptureBorder(OriginalUiText original, Brush? brush)
    {
        if (original.BorderCaptured) return;
        original.Border = CloneBrush(brush);
        original.BorderCaptured = true;
    }

    private static void CaptureForeground(OriginalUiText original, Brush? brush)
    {
        if (original.ForegroundCaptured) return;
        original.Foreground = CloneBrush(brush);
        original.ForegroundCaptured = true;
    }

    private static Brush? CloneBrush(Brush? brush)
    {
        if (brush == null) return null;
        try { return (Brush)brush.CloneCurrentValue(); }
        catch { return brush; }
    }

    private Brush? AdaptBackground(Brush? brush, bool light)
    {
        if (brush is not SolidColorBrush solid) return brush;
        var c = solid.Color;
        // Transparente und halbtransparente Template-Flächen dürfen nicht in deckende
        // Theme-Flächen umgewandelt werden. Andernfalls überdecken sie Inhalte von
        // ComboBoxen, Tabs und Diagramm-Beschriftungen bis zum nächsten Neustart.
        if (c.A < 255) return brush;
        var brightness = (c.R + c.G + c.B) / 3d;
        if (light)
        {
            if (brightness < 45) return new SolidColorBrush(Color.FromRgb(248, 250, 252));
            if (brightness < 85) return new SolidColorBrush(Color.FromRgb(255, 255, 255));
            return brush;
        }
        if (brightness > 242) return new SolidColorBrush(Color.FromRgb(23, 28, 37));
        if (brightness > 205) return new SolidColorBrush(Color.FromRgb(18, 22, 30));
        return brush;
    }

    private Brush? AdaptBorder(Brush? brush, bool light, Color accent)
    {
        if (brush is not SolidColorBrush solid) return brush;
        if (solid.Color.A < 255) return brush;
        // Semantische Farben, etwa Verbindungstypen im Diagramm, bleiben erhalten.
        // Die eigentliche Akzentfarbe wird ausschließlich über DynamicResource gesetzt.
        var brightness = (solid.Color.R + solid.Color.G + solid.Color.B) / 3d;
        if (light && brightness < 150) return new SolidColorBrush(Color.FromRgb(203, 213, 225));
        if (!light && brightness > 180) return new SolidColorBrush(Color.FromRgb(56, 66, 82));
        return brush;
    }

    private Brush? AdaptForeground(Brush? brush, bool light, Color accent)
    {
        if (brush is not SolidColorBrush solid) return brush;
        if (solid.Color.A < 255) return brush;
        // Keine pauschale Umfärbung semantischer Diagramm- und Statusfarben.
        var brightness = (solid.Color.R + solid.Color.G + solid.Color.B) / 3d;
        var neutral = Math.Abs(solid.Color.R - solid.Color.G) < 25 && Math.Abs(solid.Color.G - solid.Color.B) < 25;
        if (light)
        {
            if (brightness > 175) return new SolidColorBrush(Color.FromRgb(15, 23, 42));
            if (brightness > 100 && neutral) return new SolidColorBrush(Color.FromRgb(71, 85, 105));
            return brush;
        }
        if (brightness < 70) return new SolidColorBrush(Color.FromRgb(242, 244, 247));
        if (brightness < 125 && neutral) return new SolidColorBrush(Color.FromRgb(156, 166, 181));
        return brush;
    }

    private static readonly HashSet<string> LocalizedBindingPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "Status", "Severity", "Area", "Message", "FixKey", "Category", "Recommendation",
        "ChangeType", "DeviceType", "ConfigMode", "Module", "Mode", "Meaning", "Role", "Result"
    };

    private void ApplyLocalizationToVisualTree()
    {
        Title = LocalizationService.Get("app.title");
        if (RootLayout == null) return;
        LocalizeObjectTree(RootLayout, visibleOnly: true);
    }

    private void LocalizeObjectTree(DependencyObject root, bool visibleOnly = false)
    {
        foreach (var item in EnumerateUiObjects(root))
        {
            if (visibleOnly && item is UIElement uiElement && !uiElement.IsVisible)
                continue;
            if (item is TextBlock or Run or HeaderedContentControl or HeaderedItemsControl or ContentControl or ItemsControl or DataGrid)
            {
                LocalizeSingleObject(item);
                continue;
            }

            if (item is FrameworkElement element && (element.ToolTip != null || element.ContextMenu != null))
                LocalizeSingleObject(item);
        }
    }

    private void LocalizeSingleObject(DependencyObject item)
    {
        var original = GetOrCreateOriginal(item);

        if (item is TextBlock text)
        {
            if (!original.TextCaptured)
            {
                original.Text = text.Tag is LocalizationSource tagged ? tagged.Text : text.Text;
                original.TextCaptured = true;
            }
            text.Text = LocalizationService.TranslateText(original.Text);
        }
        else if (item is Run run)
        {
            if (!original.TextCaptured) { original.Text = run.Text; original.TextCaptured = true; }
            run.Text = LocalizationService.TranslateText(original.Text);
        }

        if (item is HeaderedContentControl headeredContent && headeredContent.Header is string headerText)
        {
            if (!original.HeaderCaptured) { original.Header = headerText; original.HeaderCaptured = true; }
            headeredContent.Header = LocalizationService.TranslateText(original.Header);
        }
        else if (item is HeaderedItemsControl headeredItems && headeredItems.Header is string itemsHeader)
        {
            if (!original.HeaderCaptured) { original.Header = itemsHeader; original.HeaderCaptured = true; }
            headeredItems.Header = LocalizationService.TranslateText(original.Header);
        }

        if (item is ContentControl contentControl && contentControl.Content is string contentText)
        {
            if (!original.ContentCaptured)
            {
                original.Content = contentControl.Tag is LocalizationSource tagged ? tagged.Text : contentText;
                original.ContentCaptured = true;
            }
            contentControl.Content = LocalizationService.TranslateText(original.Content);
        }

        if (item is FrameworkElement frameworkElement)
        {
            if (frameworkElement.ToolTip is string toolTipText)
            {
                if (!original.ToolTipCaptured) { original.ToolTip = toolTipText; original.ToolTipCaptured = true; }
                frameworkElement.ToolTip = LocalizationService.TranslateText(original.ToolTip);
            }
            else if (frameworkElement.ToolTip is ToolTip toolTip)
            {
                LocalizeObjectTree(toolTip);
            }
            else if (frameworkElement.ToolTip is DependencyObject toolTipObject)
            {
                LocalizeObjectTree(toolTipObject);
            }

            if (frameworkElement.ContextMenu is { } contextMenu)
                LocalizeObjectTree(contextMenu);
        }

        if (item is ItemsControl itemsControl && string.IsNullOrWhiteSpace(itemsControl.DisplayMemberPath))
        {
            var first = itemsControl.Items.Cast<object>().FirstOrDefault(x => x != null);
            if (first is string)
            {
                // Das Ersetzen des Templates aktualisiert die übersetzte Darstellung,
                // ohne SelectedItem/SelectedIndex wie Items.Refresh() zu verlieren.
                itemsControl.ItemTemplate = LocalizationService.CreateLocalizedStringTemplate();
            }
        }

        if (item is DataGrid dataGrid)
        {
            foreach (var column in dataGrid.Columns)
            {
                var columnOriginal = GetOrCreateOriginal(column);
                if (column.Header is string columnHeader)
                {
                    if (!columnOriginal.HeaderCaptured) { columnOriginal.Header = columnHeader; columnOriginal.HeaderCaptured = true; }
                    column.Header = LocalizationService.TranslateText(columnOriginal.Header);
                }

                if (column is DataGridTextColumn textColumn && textColumn.Binding is Binding binding)
                {
                    var path = binding.Path?.Path ?? string.Empty;
                    if (path.Equals("Meaning", StringComparison.OrdinalIgnoreCase))
                    {
                        if (binding.Converter is not CommandDescriptionConverter)
                            textColumn.Binding = CloneLocalizedBinding(binding, new CommandDescriptionConverter());
                    }
                    else if (LocalizedBindingPaths.Contains(path) && binding.Converter is not LocalizedTextConverter)
                    {
                        textColumn.Binding = CloneLocalizedBinding(binding, new LocalizedTextConverter());
                    }
                }
            }
            if (_localizedDataGrids.Add(dataGrid))
            {
                dataGrid.IsVisibleChanged += (_, _) =>
                {
                    if (!dataGrid.IsVisible) return;
                    try { dataGrid.Items.Refresh(); } catch { }
                };
            }

            // Verdeckte Tabellen werden erst beim Öffnen aktualisiert. Dadurch muss der
            // Sprachwechsel nicht sofort hunderte Befehlsbeschreibungen rendern.
            if (dataGrid.IsVisible)
            {
                try { dataGrid.Items.Refresh(); } catch { }
            }
        }
    }

    private static Binding CloneLocalizedBinding(Binding source, IValueConverter converter)
    {
        return new Binding
        {
            Path = source.Path,
            XPath = source.XPath,
            Mode = source.Mode,
            UpdateSourceTrigger = source.UpdateSourceTrigger,
            StringFormat = source.StringFormat,
            TargetNullValue = source.TargetNullValue,
            FallbackValue = source.FallbackValue,
            ConverterCulture = source.ConverterCulture,
            ConverterParameter = source.ConverterParameter,
            Converter = converter,
            ValidatesOnDataErrors = source.ValidatesOnDataErrors,
            ValidatesOnExceptions = source.ValidatesOnExceptions,
            NotifyOnValidationError = source.NotifyOnValidationError
        };
    }

    private void RunTranslationAudit()
    {
        var developerMode = GetSettingCheck("developerMode", _appSettings.DeveloperMode);
        if (!developerMode)
        {
            MessageBox.Show(this,
                LocalizationService.Get("text.die_ubersetzungsprufung_ist_nur_im_entwicklermodus_verfugbar"),
                LocalizationService.Get("text.ubersetzungsprufung"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!LocalizationService.IsEnglish)
        {
            MessageBox.Show(this,
                LocalizationService.Get("text.fur_die_ubersetzungsprufung_muss_die_anwendungssprache_auf_e"),
                LocalizationService.Get("text.ubersetzungsprufung"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ApplyLocalizationToVisualTree();
        var findings = new List<string>();
        if (RootLayout != null)
        {
            foreach (var item in EnumerateUiObjects(RootLayout))
            {
                CollectUntranslatedText(item, findings);
                if (item is FrameworkElement element && element.ToolTip is DependencyObject tip)
                    foreach (var tipItem in EnumerateUiObjects(tip)) CollectUntranslatedText(tipItem, findings);
            }
        }

        findings.AddRange(LocalizationService.ValidateEmbeddedCatalogs());
        DeveloperDiagnosticsService.WriteTranslationAudit(findings);
        var result = findings.Count == 0
            ? LocalizationService.Get("text.keine_unubersetzten_sichtbaren_texte_gefunden")
            : $"{findings.Distinct(StringComparer.Ordinal).Count()} {LocalizationService.Get("text.unubersetzte_sichtbare_texte_gefunden")}.";
        MessageBox.Show(this,
            result + Environment.NewLine + DeveloperDiagnosticsService.TranslationAuditPath,
            LocalizationService.Get("text.ubersetzungsprufung"),
            MessageBoxButton.OK,
            findings.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static void CollectUntranslatedText(DependencyObject item, ICollection<string> findings)
    {
        static void Add(string? value, ICollection<string> target)
        {
            if (!string.IsNullOrWhiteSpace(value) && LocalizationService.LooksGerman(value)) target.Add(value.Trim());
        }

        if (item is TextBlock text) Add(text.Text, findings);
        if (item is Run run) Add(run.Text, findings);
        if (item is HeaderedContentControl hcc && hcc.Header is string h1) Add(h1, findings);
        if (item is HeaderedItemsControl hic && hic.Header is string h2) Add(h2, findings);
        if (item is ContentControl cc && cc.Content is string content) Add(content, findings);
        if (item is FrameworkElement fe && fe.ToolTip is string tip) Add(tip, findings);
        if (item is DataGrid grid)
            foreach (var column in grid.Columns)
                if (column.Header is string header) Add(header, findings);
    }

    private OriginalUiText GetOrCreateOriginal(object owner)
    {
        if (!_originalUiTexts.TryGetValue(owner, out var original))
        {
            original = new OriginalUiText();
            _originalUiTexts[owner] = original;
        }
        return original;
    }

    private static IEnumerable<DependencyObject> EnumerateUiObjects(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current)) continue;
            yield return current;

            try
            {
                var visualCount = VisualTreeHelper.GetChildrenCount(current);
                for (var i = 0; i < visualCount; i++) queue.Enqueue(VisualTreeHelper.GetChild(current, i));
            }
            catch
            {
                // Manche logischen WPF-Objekte sind keine Visuals.
            }

            foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
                queue.Enqueue(child);
        }
    }

    private void ExportApplicationSettings()
    {
        ApplySettingsFromPage(false);
        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.Get("settings.export"),
            Filter = "JSON (*.json)|*.json",
            FileName = "cisco_konfigurator_settings.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        ApplicationSettingsService.Export(dialog.FileName, _appSettings);
        MessageBox.Show(this, LocalizationService.Get("text.einstellungen_wurden_exportiert"), LocalizationService.Get("navigation.settings"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ImportApplicationSettings()
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("settings.import"),
            Filter = "JSON (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ApplicationSettingsService.Import(dialog.FileName);
            _appSettings = ApplicationSettingsService.Current;
            PopulateSettingsPage();
            LocalizationService.SetLanguage(_appSettings.Language);
            ApplyRuntimeSettings();
            MessageBox.Show(this, LocalizationService.Get("text.einstellungen_wurden_importiert"), LocalizationService.Get("navigation.settings"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, LocalizationService.Get("text.fehler"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetApplicationSettings()
    {
        if (MessageBox.Show(this, LocalizationService.Get("text.alle_einstellungen_auf_standardwerte_zurucksetzen"), LocalizationService.Get("settings.defaults"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _appSettings = new ApplicationSettings();
        ApplicationSettingsService.Save(_appSettings);
        PopulateSettingsPage();
        LocalizationService.SetLanguage(_appSettings.Language);
        ApplyRuntimeSettings();
    }

    private void PopulateSettingsPage()
    {
        _applyingSettings = true;
        try
        {
            SetSettingCombo("language", LanguageToDisplay(_appSettings.Language));
            SetSettingCombo("reportLanguage", LanguageToDisplay(_appSettings.ReportLanguage));
            SetSettingCombo("startPage", _appSettings.StartPage);
            SetSettingCheck("loadLastProject", _appSettings.LoadLastProject);
            SetSettingCheck("confirmReset", _appSettings.ConfirmReset);
            SetSettingCombo("theme", _appSettings.Theme);
            SetSettingCombo("accentColor", _appSettings.AccentColor);
            SetSettingCombo("fontSize", _appSettings.FontSize);
            SetSettingCheck("compactMode", _appSettings.CompactMode);
            SetSettingCheck("animations", _appSettings.AnimationsEnabled);
            SetSettingCheck("tooltips", _appSettings.TooltipsEnabled);
            SetSettingCheck("livePreview", _appSettings.LivePreviewEnabled);
            SetSettingCheck("collapsibleNavigation", _appSettings.CollapsibleNavigation);
            SetSettingCombo("defaultDevice", _appSettings.DefaultDeviceType);
            SetSettingCombo("defaultMode", _appSettings.DefaultConfigMode);
            SetSettingCombo("defaultPlatform", _appSettings.DefaultPlatform);
            SetSettingText("defaultInterface", _appSettings.DefaultInterfacePattern);
            SetSettingCombo("defaultIpStack", _appSettings.DefaultIpStack);
            SetSettingCheck("includeComments", _appSettings.IncludeComments);
            SetSettingCheck("includeSeparators", _appSettings.IncludeSectionSeparators);
            SetSettingCheck("includeEnable", _appSettings.IncludeEnable);
            SetSettingCheck("includeConfigureTerminal", _appSettings.IncludeConfigureTerminal);
            SetSettingCheck("includeEnd", _appSettings.IncludeEnd);
            SetSettingCheck("includeWriteMemory", _appSettings.IncludeWriteMemory);
            SetSettingCombo("validationMode", _appSettings.ValidationMode);
            SetSettingCombo("autoFixMode", _appSettings.AutoFixMode);
            SetSettingText("exportFolder", _appSettings.DefaultExportFolder);
            SetSettingText("fileNamePattern", _appSettings.ExportFileNamePattern);
            SetSettingCombo("lineEndings", _appSettings.LineEndings);
            SetSettingCheck("sortByModules", _appSettings.SortConfigurationByModules);
            SetSettingCheck("keepUnknown", _appSettings.KeepUnknownCommands);
            SetSettingCheck("includeCustom", _appSettings.IncludeCustomCommands);
            SetSettingCheck("timestampFile", _appSettings.TimestampInFileName);
            SetSettingCheck("exportPeer", _appSettings.ExportPeerConfiguration);
            SetSettingCheck("generateRollback", _appSettings.GenerateRollbackFile);
            SetSettingCheck("exportReports", _appSettings.ExportReportsTogether);
            SetSettingCheck("autoSave", _appSettings.AutoSaveEnabled);
            SetSettingText("autoSaveInterval", _appSettings.AutoSaveIntervalSeconds.ToString(CultureInfo.InvariantCulture));
            SetSettingText("backupCount", _appSettings.BackupCount.ToString(CultureInfo.InvariantCulture));
            SetSettingText("backupFolder", _appSettings.BackupFolder);
            SetSettingCheck("saveOnExit", _appSettings.SaveProjectOnExit);
            SetSettingCheck("restoreCrash", _appSettings.RestoreAfterCrash);
            SetSettingCheck("history", _appSettings.HistoryEnabled);
            SetSettingText("historyLimit", _appSettings.HistoryLimit.ToString(CultureInfo.InvariantCulture));
            SetSettingText("sshPort", _appSettings.DefaultSshPort.ToString(CultureInfo.InvariantCulture));
            SetSettingText("connectionTimeout", _appSettings.ConnectionTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            SetSettingText("commandTimeout", _appSettings.CommandTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            SetSettingText("commandDelay", _appSettings.CommandDelayMilliseconds.ToString(CultureInfo.InvariantCulture));
            SetSettingCheck("backupBeforeTransfer", _appSettings.BackupBeforeTransfer);
            SetSettingCheck("abortOnError", _appSettings.AbortTransferOnError);
            SetSettingCheck("showBeforeSend", _appSettings.ShowCommandsBeforeSend);
            SetSettingCheck("storePasswords", _appSettings.StorePasswords);
            SetSettingCheck("sessionLogging", _appSettings.SessionLoggingEnabled);
            SetSettingText("deviceBackupFolder", _appSettings.DeviceBackupFolder);
            SetSettingCheck("autoLayout", _appSettings.AutomaticDiagramLayout);
            SetSettingCheck("snapGrid", _appSettings.SnapDiagramToGrid);
            SetSettingCheck("showLinkTypes", _appSettings.ShowConnectionTypes);
            SetSettingCheck("showInterfaces", _appSettings.ShowInterfaceNames);
            SetSettingCheck("showIps", _appSettings.ShowIpAddresses);
            SetSettingCheck("showVlans", _appSettings.ShowVlans);
            SetSettingCombo("reportFormat", _appSettings.DefaultReportFormat);
            SetSettingText("company", _appSettings.CompanyName);
            SetSettingText("projectManager", _appSettings.ProjectManager);
            SetSettingText("reportLogo", _appSettings.ReportLogoPath);
            SetSettingCombo("pageSize", _appSettings.PageSize);
            SetSettingCombo("pageOrientation", _appSettings.PageOrientation);
            SetSettingCheck("strictImport", _appSettings.StrictImportValidation);
            SetSettingCheck("developerMode", _appSettings.DeveloperMode);
            SetSettingCheck("diagnosticDetails", _appSettings.IncludeDiagnosticDetails);
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    private string GetSettingCombo(string key, string fallback) =>
        _settingsControls.TryGetValue(key, out var control) && control is ComboBox combo
            ? combo.SelectedItem?.ToString() ?? fallback
            : fallback;

    private string GetSettingText(string key, string fallback) =>
        _settingsControls.TryGetValue(key, out var control) && control is TextBox box ? box.Text ?? fallback : fallback;

    private bool GetSettingCheck(string key, bool fallback) =>
        _settingsControls.TryGetValue(key, out var control) && control is CheckBox check ? check.IsChecked == true : fallback;

    private int GetSettingInt(string key, int fallback, int min, int max) =>
        int.TryParse(GetSettingText(key, fallback.ToString(CultureInfo.InvariantCulture)), out var value) ? Math.Clamp(value, min, max) : fallback;

    private void SetSettingCombo(string key, string value)
    {
        if (_settingsControls.TryGetValue(key, out var control) && control is ComboBox combo)
            combo.SelectedItem = combo.Items.Cast<object>().FirstOrDefault(x => string.Equals(x?.ToString(), value, StringComparison.OrdinalIgnoreCase)) ?? combo.Items.Cast<object>().FirstOrDefault();
    }

    private void SetSettingText(string key, string value)
    {
        if (_settingsControls.TryGetValue(key, out var control) && control is TextBox box) box.Text = value ?? string.Empty;
    }

    private void SetSettingCheck(string key, bool value)
    {
        if (_settingsControls.TryGetValue(key, out var control) && control is CheckBox check) check.IsChecked = value;
    }

    private static string LanguageToDisplay(string? language) => (language ?? "system").ToLowerInvariant() switch
    {
        "de-de" or "de" => "Deutsch",
        "en-us" or "en" => "English",
        _ => "Systemsprache"
    };

    private static string DisplayToLanguage(string? display) => (display ?? "Systemsprache") switch
    {
        "Deutsch" => "de-DE",
        "English" => "en-US",
        _ => "system"
    };

    private static string ResolveTheme(string? theme)
    {
        if (!string.Equals(theme, "System", StringComparison.OrdinalIgnoreCase)) return theme ?? "Dunkel";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue > 0 ? "Hell" : "Dunkel";
        }
        catch
        {
            return "Dunkel";
        }
    }

    private static Color GetAccentColor(string? name) => (name ?? "Orange") switch
    {
        "Blau" => Color.FromRgb(59, 130, 246),
        "Grün" => Color.FromRgb(34, 197, 94),
        "Violett" => Color.FromRgb(139, 92, 246),
        _ => Color.FromRgb(232, 121, 26)
    };

    private static Color Lighten(Color c, byte amount) => Color.FromRgb(
        (byte)Math.Min(255, c.R + amount),
        (byte)Math.Min(255, c.G + amount),
        (byte)Math.Min(255, c.B + amount));

    private static Color Darken(Color c, byte amount) => Color.FromRgb(
        (byte)Math.Max(0, c.R - amount),
        (byte)Math.Max(0, c.G - amount),
        (byte)Math.Max(0, c.B - amount));

    private static bool IsAccentLike(Color color)
    {
        var palette = new[]
        {
            Color.FromRgb(232, 121, 26),
            Color.FromRgb(59, 130, 246),
            Color.FromRgb(34, 197, 94),
            Color.FromRgb(139, 92, 246)
        };

        foreach (var baseColor in palette)
        {
            if (ColorDistance(color, baseColor) <= 42) return true;
            if (ColorDistance(color, Lighten(baseColor, 35)) <= 42) return true;
            if (ColorDistance(color, Lighten(baseColor, 18)) <= 42) return true;
            if (ColorDistance(color, Darken(baseColor, 38)) <= 42) return true;
        }
        return false;
    }

    private static double ColorDistance(Color left, Color right)
    {
        var dr = left.R - right.R;
        var dg = left.G - right.G;
        var db = left.B - right.B;
        return Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }

    private void SetBrushResource(string key, Color color)
    {
        // Brushes loaded from XAML styles can be frozen by WPF. Mutating the
        // existing SolidColorBrush would then throw InvalidOperationException.
        // Replacing the resource is safe and updates every DynamicResource user.
        var replacement = new SolidColorBrush(color);

        if (Resources.Contains(key))
        {
            Resources[key] = replacement;
            return;
        }

        if (Application.Current?.Resources.Contains(key) == true)
        {
            Application.Current.Resources[key] = replacement;
            return;
        }

        Resources[key] = replacement;
    }

    private string ApplyConfigurationOutputSettings(string configuration)
    {
        var lines = (configuration ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        if (!_appSettings.IncludeComments)
            lines = lines.Where(x => !x.TrimStart().StartsWith("!", StringComparison.Ordinal)).ToList();
        else if (!_appSettings.IncludeSectionSeparators)
            lines = lines.Where(x => !Regex.IsMatch(x.Trim(), @"^!\s*[=\-]{3,}\s*$")).ToList();

        if (!_appSettings.IncludeEnable) lines.RemoveAll(x => x.Trim().Equals("enable", StringComparison.OrdinalIgnoreCase));
        if (!_appSettings.IncludeConfigureTerminal) lines.RemoveAll(x => x.Trim().Equals("configure terminal", StringComparison.OrdinalIgnoreCase) || x.Trim().Equals("conf t", StringComparison.OrdinalIgnoreCase));
        if (!_appSettings.IncludeEnd) lines.RemoveAll(x => x.Trim().Equals("end", StringComparison.OrdinalIgnoreCase));
        if (!_appSettings.IncludeWriteMemory) lines.RemoveAll(x => x.Trim().Equals("write memory", StringComparison.OrdinalIgnoreCase) || x.Trim().Equals("copy running-config startup-config", StringComparison.OrdinalIgnoreCase));

        var newline = _appSettings.LineEndings.Equals("Unix (LF)", StringComparison.OrdinalIgnoreCase) ? "\n" : "\r\n";
        return string.Join(newline, lines).TrimEnd() + newline;
    }

    private string BuildExportFileName()
    {
        var hostname = GetFieldValue("hostname");
        if (string.IsNullOrWhiteSpace(hostname)) hostname = "device";
        var pattern = string.IsNullOrWhiteSpace(_appSettings.ExportFileNamePattern) ? "cisco_config_{hostname}" : _appSettings.ExportFileNamePattern;
        var name = pattern.Replace("{hostname}", hostname, StringComparison.OrdinalIgnoreCase)
            .Replace("{device}", DeviceTypeCombo.SelectedItem?.ToString() ?? "device", StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", DateTime.Now.ToString("yyyyMMdd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", DateTime.Now.ToString("HHmmss"), StringComparison.OrdinalIgnoreCase);
        if (_appSettings.TimestampInFileName && !name.Contains(DateTime.Now.ToString("yyyyMMdd"), StringComparison.Ordinal))
            name += "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        foreach (var invalid in IOPath.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) name += ".txt";
        return name;
    }
}
