using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace VioraManager;

public partial class MainWindow
{
    private sealed class LaunchHistoryEntry
    {
        public string Account { get; set; } = "";
        public long PlaceId { get; set; }
        public string? JobId { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string Detail => TimestampUtc.ToLocalTime().ToString("g") + " · " +
            Account + " · Place " + PlaceId + (JobId is null ? "" : " · Server " + JobId);
    }

    private static readonly string HistoryFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Viora", "Manager", "launch-history.json");

    private int LaunchDelaySeconds
    {
        get
        {
            string? value = (LaunchDelayInput.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            return int.TryParse(value, out int seconds) && seconds is >= 2 and <= 10 ? seconds : 4;
        }
    }

    private void RecordLaunch(SavedRobloxAccount account, long? placeId, string? jobId)
    {
        if (placeId is not > 0) return;
        try
        {
            var history = ReadLaunchHistory().ToList();
            history.Insert(0, new LaunchHistoryEntry
            {
                Account = account.DisplayLabel,
                PlaceId = placeId.Value,
                JobId = jobId,
                TimestampUtc = DateTime.UtcNow
            });
            Directory.CreateDirectory(Path.GetDirectoryName(HistoryFile)!);
            string temp = HistoryFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(history.Take(50).ToArray()));
            File.Move(temp, HistoryFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Launch history is optional. It must never prevent opening Roblox.
            AccountLaunchStatus.Text = "Game opened, but history couldn't be saved: " + ex.Message;
        }
    }

    private IReadOnlyList<LaunchHistoryEntry> ReadLaunchHistory()
    {
        if (!File.Exists(HistoryFile)) return Array.Empty<LaunchHistoryEntry>();
        try
        {
            if (new FileInfo(HistoryFile).Length > 200_000)
                return Array.Empty<LaunchHistoryEntry>();
            return (JsonSerializer.Deserialize<List<LaunchHistoryEntry>>(
                File.ReadAllText(HistoryFile)) ?? new List<LaunchHistoryEntry>())
                .Where(x => x.PlaceId > 0).Take(50).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Array.Empty<LaunchHistoryEntry>();
        }
    }

    private void LaunchHistory_Click(object sender, RoutedEventArgs e)
    {
        var records = ReadLaunchHistory();
        if (records.Count == 0)
        {
            MessageBox.Show(this, "No recorded game-start links yet. Use Play first.",
                "Viora launch history", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var panel = new DockPanel { Margin = new Thickness(16) };
        var header = new TextBlock
        {
            Text = "Recent game-start links (not verified game joins)",
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        var apply = new Button
        {
            Content = "Use selected Place / Job ID", Padding = new Thickness(12, 9),
            Background = (System.Windows.Media.Brush)FindResource("Accent"),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(28, 13, 43)),
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(apply, Dock.Bottom);
        panel.Children.Add(apply);
        var list = new ListBox { ItemsSource = records, DisplayMemberPath = "Detail" };
        panel.Children.Add(list);
        var dialog = new Window
        {
            Title = "Viora — Launch History", Width = 700, Height = 480,
            MinWidth = 550, MinHeight = 350, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };
        apply.Click += (_, _) =>
        {
            if (list.SelectedItem is not LaunchHistoryEntry chosen) return;
            QuickPlaceInput.Text = chosen.PlaceId.ToString();
            QuickJobInput.Text = chosen.JobId ?? "";
            AccountLaunchStatus.Text = "Loaded a previous game-start target from history.";
            dialog.Close();
        };
        dialog.ShowDialog();
    }

    private void ImportAccounts_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Viora account metadata (no browser sessions)",
            Filter = "JSON account backups (*.json)|*.json",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 1_000_000)
                throw new InvalidDataException("Backup exceeds the 1 MB metadata limit.");
            var imported = JsonSerializer.Deserialize<List<SavedRobloxAccount>>(
                File.ReadAllText(dialog.FileName)) ??
                throw new InvalidDataException("Backup is empty.");
            if (imported.Count > 1000)
                throw new InvalidDataException("Too many accounts in the backup.");

            var existing = _connectedAccounts.ToList();
            var ids = existing.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var names = existing.Select(x => x.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var profileKeys = existing.Where(x => !x.ManagedBrowserProfile)
                .Select(x => x.Browser + ":" + x.ProfileDirectory)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int added = 0, skipped = 0;
            foreach (var item in imported)
            {
                bool valid = item is not null &&
                    Guid.TryParseExact(item.Id, "N", out _) &&
                    Regex.IsMatch(item.Username ?? "", @"^[A-Za-z0-9_]{3,20}$") &&
                    item.Browser is "Edge" or "Chrome" or "Brave" &&
                    item.ProfileDirectory is not null &&
                    (item.ManagedBrowserProfile ||
                        Regex.IsMatch(item.ProfileDirectory, @"^(Default|Profile [1-9][0-9]{0,3})$")) &&
                    item.Alias?.Length <= 48 && item.Notes?.Length <= 500 &&
                    item.RobloxUsername?.Length <= 20 &&
                    item.Group?.Length <= 40 && item.Tags?.Length <= 140 &&
                    (item.PlaceId is null or > 0);
                if (!valid)
                    throw new InvalidDataException("The backup contains invalid account metadata.");

                if (ids.Contains(item!.Id) || names.Contains(item.Username) ||
                    (!item.ManagedBrowserProfile &&
                     profileKeys.Contains(item.Browser + ":" + item.ProfileDirectory)))
                {
                    skipped++;
                    continue;
                }
                // Public profile labels imported from a file are untrusted and
                // must be looked up afresh before showing avatar/user ID.
                item.PublicUserId = null;
                item.PublicDisplayName = "";
                existing.Add(item);
                ids.Add(item.Id);
                names.Add(item.Username);
                if (!item.ManagedBrowserProfile)
                    profileKeys.Add(item.Browser + ":" + item.ProfileDirectory);
                added++;
                if (existing.Count >= 1000) break;
            }
            if (added == 0)
            {
                MessageBox.Show(this, "No new accounts to import (" + skipped + " already exist).",
                    "Import complete", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show(this,
                "Merge " + added + " new local account shortcuts (" + skipped + " duplicates skipped)?\n\n" +
                "This imports labels/settings only, NOT Roblox browser login sessions. " +
                "On a different PC, re-login to every imported account through Roblox.",
                "Import account metadata", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            Directory.CreateDirectory(Path.GetDirectoryName(AccountsFile)!);
            var temp = AccountsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(existing,
                new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(AccountsFile))
                File.Copy(AccountsFile, AccountsFile + ".bak", true);
            File.Move(temp, AccountsFile, true);
            ReloadConnectedAccounts();
            _ = RefreshPublicProfilesAsync();
            AccountLaunchStatus.Text = "Imported " + added + " metadata shortcuts; skipped " +
                skipped + " duplicates. Browser sign-ins were not imported.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidDataException or JsonException)
        {
            MessageBox.Show(this, "Account import failed; current account list was not changed.\n\n" +
                ex.Message, "Viora Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
