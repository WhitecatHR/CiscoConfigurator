using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CiscoConfigGuiWpf;

public partial class MainWindow
{
    private void OpenProjectVersionManager()
    {
        SyncProjectEditors();
        NormalizeProjectCollections();

        var window = new Window
        {
            Owner = this,
            Title = LocalizationService.Get("versioning.title", "Projektversionierung"),
            Width = 1120,
            Height = 720,
            MinWidth = 850,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(11, 14, 19)),
            Foreground = Brushes.White
        };

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("versioning.title", "Projektversionierung"),
            FontSize = 22,
            FontWeight = FontWeights.Bold
        });
        header.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("versioning.subtitle", "Projektstände erstellen, kommentieren, vergleichen und wiederherstellen."),
            Foreground = new SolidColorBrush(Color.FromRgb(156, 166, 181)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        root.Children.Add(header);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        var versionGrid = new DataGrid
        {
            ItemsSource = _currentProject.VersionHistory,
            IsReadOnly = true,
            AutoGenerateColumns = false,
            SelectionMode = DataGridSelectionMode.Single,
            Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 56, 70))
        };
        versionGrid.Columns.Add(new DataGridTextColumn
        {
            Header = LocalizationService.Get("versioning.created", "Erstellt"),
            Binding = new Binding(nameof(ProjectVersionEntry.DisplayCreated)),
            Width = 145
        });
        versionGrid.Columns.Add(new DataGridTextColumn
        {
            Header = LocalizationService.Get("versioning.label", "Bezeichnung"),
            Binding = new Binding(nameof(ProjectVersionEntry.Label)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        versionGrid.Columns.Add(new DataGridTextColumn
        {
            Header = LocalizationService.Get("versioning.type", "Typ"),
            Binding = new Binding(nameof(ProjectVersionEntry.DisplayType)),
            Width = 95
        });
        versionGrid.Columns.Add(new DataGridTextColumn
        {
            Header = LocalizationService.Get("versioning.devices", "Geräte"),
            Binding = new Binding(nameof(ProjectVersionEntry.DeviceCount)),
            Width = 65
        });
        content.Children.Add(versionGrid);

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(right, 2);
        content.Children.Add(right);

        right.Children.Add(new TextBlock
        {
            Text = LocalizationService.Get("versioning.metadata", "Versionsdaten"),
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var labelBox = new TextBox { Margin = new Thickness(0, 0, 0, 6), MinHeight = 34 };
        Grid.SetRow(labelBox, 1);
        right.Children.Add(labelBox);
        var commentBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 80,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(commentBox, 2);
        right.Children.Add(commentBox);
        var comparisonBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            Background = new SolidColorBrush(Color.FromRgb(14, 18, 25)),
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 56, 70)),
            Padding = new Thickness(10)
        };
        Grid.SetRow(comparisonBox, 3);
        right.Children.Add(comparisonBox);

        versionGrid.SelectionChanged += (_, _) =>
        {
            if (versionGrid.SelectedItem is not ProjectVersionEntry selected) return;
            labelBox.Text = selected.Label;
            commentBox.Text = selected.Comment;
            comparisonBox.Text = LocalizationService.Get("versioning.select_compare", "Mit 'Vergleichen' werden Änderungen zum aktuellen Projekt angezeigt.");
        };

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var createButton = new Button { Content = LocalizationService.Get("versioning.create", "Version erstellen"), Style = TryFindResource("PrimaryButtonStyle") as Style };
        var updateButton = new Button { Content = LocalizationService.Get("versioning.update_metadata", "Daten übernehmen") };
        var compareButton = new Button { Content = LocalizationService.Get("versioning.compare", "Vergleichen") };
        var restoreButton = new Button { Content = LocalizationService.Get("versioning.restore", "Wiederherstellen") };
        var deleteButton = new Button { Content = LocalizationService.Get("versioning.delete", "Löschen") };
        var closeButton = new Button { Content = LocalizationService.Get("common.close", "Schließen") };

        createButton.Click += (_, _) =>
        {
            var data = ShowVersionInputDialog(window);
            if (data == null) return;
            SyncProjectEditors();
            var entry = ProjectVersioningService.CreateVersion(
                _currentProject,
                data.Value.Label,
                data.Value.Comment,
                false,
                _appSettings.HistoryLimit,
                skipDuplicate: false);
            versionGrid.Items.Refresh();
            versionGrid.SelectedItem = entry;
            ScheduleAutoSave();
        };
        updateButton.Click += (_, _) =>
        {
            if (versionGrid.SelectedItem is not ProjectVersionEntry selected) return;
            selected.Label = string.IsNullOrWhiteSpace(labelBox.Text) ? selected.Label : labelBox.Text.Trim();
            selected.Comment = commentBox.Text?.Trim() ?? string.Empty;
            versionGrid.Items.Refresh();
            ScheduleAutoSave();
        };
        compareButton.Click += (_, _) =>
        {
            if (versionGrid.SelectedItem is not ProjectVersionEntry selected)
            {
                comparisonBox.Text = LocalizationService.Get("versioning.select_version", "Bitte zuerst einen Versionsstand auswählen.");
                return;
            }
            SyncProjectEditors();
            comparisonBox.Text = ProjectVersioningService.Compare(
                _currentProject,
                selected,
                LocalizationService.CurrentLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        };
        restoreButton.Click += (_, _) =>
        {
            if (versionGrid.SelectedItem is not ProjectVersionEntry selected) return;
            if (MessageBox.Show(window,
                    LocalizationService.Format("versioning.restore_confirm", selected.Label),
                    LocalizationService.Get("versioning.restore", "Wiederherstellen"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            SyncProjectEditors();
            ProjectVersioningService.CreateVersion(
                _currentProject,
                LocalizationService.Get("versioning.before_restore", "Vor Wiederherstellung"),
                LocalizationService.Format("versioning.before_restore_comment", selected.Label),
                true,
                _appSettings.HistoryLimit,
                skipDuplicate: false);
            _currentProject = ProjectVersioningService.RestoreVersion(_currentProject, selected);
            NormalizeProjectCollections();
            RebindProjectCollections();
            RefreshProjectEditors();
            RefreshNetworkDiagram();
            ScheduleAutoSave();
            window.Close();
            MessageBox.Show(this,
                LocalizationService.Get("versioning.restored", "Der ausgewählte Projektstand wurde wiederhergestellt. Speichere das Projekt, um die Änderung dauerhaft zu übernehmen."),
                LocalizationService.Get("versioning.title", "Projektversionierung"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        };
        deleteButton.Click += (_, _) =>
        {
            if (versionGrid.SelectedItem is not ProjectVersionEntry selected) return;
            if (MessageBox.Show(window,
                    LocalizationService.Format("versioning.delete_confirm", selected.Label),
                    LocalizationService.Get("versioning.delete", "Löschen"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _currentProject.VersionHistory.Remove(selected);
            versionGrid.Items.Refresh();
            labelBox.Clear();
            commentBox.Clear();
            comparisonBox.Clear();
            ScheduleAutoSave();
        };
        closeButton.Click += (_, _) => window.Close();

        foreach (var button in new[] { createButton, updateButton, compareButton, restoreButton, deleteButton, closeButton })
        {
            button.Margin = new Thickness(3);
            actions.Children.Add(button);
        }
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        window.Content = root;
        window.ShowDialog();
    }

    private (string Label, string Comment)? ShowVersionInputDialog(Window owner)
    {
        var dialog = new Window
        {
            Owner = owner,
            Title = LocalizationService.Get("versioning.create", "Version erstellen"),
            Width = 520,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(11, 14, 19)),
            Foreground = Brushes.White
        };
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = LocalizationService.Get("versioning.label", "Bezeichnung"), Margin = new Thickness(0, 0, 0, 4) });
        var labelBox = new TextBox { Text = $"Version {_currentProject.VersionHistory.Count + 1}", MinHeight = 34 };
        Grid.SetRow(labelBox, 1);
        root.Children.Add(labelBox);
        var commentLabel = new TextBlock { Text = LocalizationService.Get("versioning.comment", "Kommentar"), Margin = new Thickness(0, 10, 0, 4) };
        Grid.SetRow(commentLabel, 2);
        root.Children.Add(commentLabel);
        var commentBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(commentBox, 3);
        root.Children.Add(commentBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = LocalizationService.Get("common.ok", "OK"), IsDefault = true, MinWidth = 90 };
        var cancel = new Button { Content = LocalizationService.Get("common.cancel", "Abbrechen"), IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);
        dialog.Content = root;
        return dialog.ShowDialog() == true
            ? (labelBox.Text?.Trim() ?? string.Empty, commentBox.Text?.Trim() ?? string.Empty)
            : null;
    }
}
