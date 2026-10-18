using System.Windows;
using System.Windows.Controls;

namespace Viora;

// UI deliberately exposes only tag management. Privileges live in the database:
// only the verified "owner" role can lookup users or change tags.
public partial class MainWindow
{
    private VioraManagedAccount? _selectedOwnerAccount;
    private bool _ownerToolBusy;

    private bool OwnerSessionReady =>
        _isSignedIn && _onlineProfile?.Role == "owner" &&
        _onlineService?.IsSignedIn == true;

    private async void OwnerLookup_Click(object sender, RoutedEventArgs e)
    {
        if (!OwnerSessionReady || _ownerToolBusy) return;
        _ownerToolBusy = true;
        OwnerLookupButton.IsEnabled = false;
        OwnerAssignButton.IsEnabled = false;
        _selectedOwnerAccount = null;
        OwnerSelectedAccount.Text = "Looking up user…";
        try
        {
            var found = await _onlineService!.OwnerLookupAsync(OwnerSearchUsername.Text.Trim());
            if (!OwnerSessionReady) return;
            _selectedOwnerAccount = found;
            OwnerSelectedAccount.Text = found is null
                ? "No online account found with that exact username."
                : found.Username + " · " + found.IdLabel + " · Current tag: " + found.RoleLabel;
            OwnerPanelStatus.Text = found is null ? "Check the spelling and try again."
                : found.Role == "owner" ? "Owner tags cannot be managed in this panel."
                : "Verify this person's ID before assigning their new tag.";
            OwnerAssignButton.IsEnabled = found is not null && found.Role != "owner";
        }
        catch (Exception ex)
        {
            OwnerPanelStatus.Text = "Lookup failed: " + ex.Message;
            OwnerSelectedAccount.Text = "Search again to select a user.";
        }
        finally { _ownerToolBusy = false; OwnerLookupButton.IsEnabled = OwnerSessionReady; }
    }

    private async void OwnerAssign_Click(object sender, RoutedEventArgs e)
    {
        if (!OwnerSessionReady || _ownerToolBusy || _selectedOwnerAccount is null) return;
        var selected = _selectedOwnerAccount;
        var nextRole = (OwnerRoleChoice.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        if (selected.Role == "owner" || selected.PublicId == _onlineProfile!.PublicId ||
            nextRole is not ("member" or "tester" or "developer" or "administrator")) return;
        var pretty = nextRole == "member" ? "Member (remove tag)" : nextRole;
        if (MessageBox.Show(this,
            "Apply the tag " + pretty + " to " + selected.Username +
            " (" + selected.IdLabel + ")?\n\nThis modifies their public Viora Online tag.",
            "Confirm tag assignment", MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _ownerToolBusy = true;
        OwnerLookupButton.IsEnabled = false;
        OwnerAssignButton.IsEnabled = false;
        try
        {
            // Postgres rechecks ownership on every call and audits the change.
            var updated = await _onlineService!.OwnerSetRoleAsync(selected.PublicId, nextRole);
            if (!OwnerSessionReady) return;
            _selectedOwnerAccount = updated;
            OwnerSelectedAccount.Text = updated.Username + " · " + updated.IdLabel +
                " · Current tag: " + updated.RoleLabel;
            OwnerPanelStatus.Text = "Saved by Supabase. Tag changes are recorded server-side.";
            // Newly assigned labels are visible when the public leaderboard reloads.
            await RefreshOnlineLeaderboardAsync();
        }
        catch (Exception ex)
        {
            OwnerPanelStatus.Text = "Tag update failed: " + ex.Message;
            _selectedOwnerAccount = null; // Never operate on stale account state after an error.
            OwnerSelectedAccount.Text = "Search again to verify the account's current tag.";
        }
        finally
        {
            _ownerToolBusy = false;
            OwnerLookupButton.IsEnabled = OwnerSessionReady;
            OwnerAssignButton.IsEnabled = OwnerSessionReady && _selectedOwnerAccount is { Role: not "owner" };
        }
    }
}
