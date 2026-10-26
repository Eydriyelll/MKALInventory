using MkalInventoryApp.Models;
using MkalInventoryApp.Services;

namespace MkalInventoryApp.Views;

public partial class AdminDashboardPage : ContentPage
{
    private static readonly Color Orange = Color.FromArgb("#FF6F00");
    private static readonly Color Ink = Color.FromArgb("#1F2933");
    private static readonly Color Good = Color.FromArgb("#2E7D32");
    private static readonly Color Bad = Color.FromArgb("#C62828");

    private readonly ServiceHub _hub;
    private readonly SessionService _session;
    private readonly Button[] _tabs;
    private readonly View[] _panels;
    private readonly bool[] _dirty = { true, true, true, true, true };
    private readonly Debouncer _toolSearch = new();
    private readonly Debouncer _auditSearch = new();

    private Item? _selected;
    private int _currentTab;
    private bool _ready;
    private int _bannerVersion;

    private User Admin => _session.CurrentUser!;

    public AdminDashboardPage(ServiceHub hub, SessionService session)
    {
        InitializeComponent();
        _hub = hub;
        _session = session;
        NavigationPage.SetHasBackButton(this, false);

        _tabs = new[] { tab0, tab1, tab2, tab3, tab4 };
        _panels = new View[] { pnl0, pnl1, pnl2, pnl3, pnl4 };

        pckCategory.ItemsSource = ToolCategories.All;
        pckCategory.SelectedIndex = ToolCategories.All.Length - 1;
        pckFilterCategory.ItemsSource = new[] { "All categories" }.Concat(ToolCategories.All).ToList();
        pckFilterCategory.SelectedIndex = 0;
        pckFilterStatus.ItemsSource = new[] { "All statuses" }.Concat(ToolStatus.All).ToList();
        pckFilterStatus.SelectedIndex = 0;
        pckSort.ItemsSource = new[] { "Name", "Tool ID", "Status", "Calibration due" };
        pckSort.SelectedIndex = 0;
        pckNewRole.ItemsSource = new[] { "Field technician", "Administrator / QTP" };
        pckNewRole.SelectedIndex = 0;
        pckNewTitle.ItemsSource = JobTitles.All;
        pckNewTitle.SelectedIndex = 2;
        dpCalibration.Date = DateTime.Today.AddMonths(6);

        lblWho.Text = $"Signed in as {Admin.FullName}";
        ApplyTabStyles(0);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _ready = true;
        await GuardAsync(async () =>
        {
            await _hub.Compliance.RunPolicyScanAsync(Admin);
            MarkAllDirty();
            await LoadTabAsync(_currentTab);
        });
    }

    // ------------------------------------------------------------------
    // Feedback, tabs and loading
    // ------------------------------------------------------------------
    private async void ShowBanner(string message, bool ok)
    {
        var version = ++_bannerVersion;
        lblBanner.Text = message;
        bnr.BackgroundColor = ok ? Good : Bad;
        bnr.IsVisible = true;
        await Task.Delay(7000);
        if (version == _bannerVersion) bnr.IsVisible = false;
    }

    private void OnBannerTapped(object? sender, TappedEventArgs e) => bnr.IsVisible = false;
    private void ShowResult(OpResult r) => ShowBanner(r.Message, r.Ok);

    private async Task GuardAsync(Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex)
        {
            CrashLog.Write("Admin", ex);
            ShowBanner($"Something went wrong: {ex.Message}", false);
        }
    }

    private void ApplyTabStyles(int index)
    {
        for (int i = 0; i < _panels.Length; i++)
        {
            _panels[i].IsVisible = i == index;
            _tabs[i].BackgroundColor = i == index ? Orange : Colors.Transparent;
            _tabs[i].TextColor = i == index ? Colors.White : Ink;
        }
    }

    private async void OnTabClicked(object? sender, EventArgs e)
    {
        var index = Array.IndexOf(_tabs, (Button)sender!);
        if (index < 0) return;
        _currentTab = index;
        ApplyTabStyles(index);
        await GuardAsync(() => LoadTabAsync(index));
    }

    private void MarkAllDirty() { for (int i = 0; i < _dirty.Length; i++) _dirty[i] = true; }

    /// <summary>Tabs load only when opened, and only again after something changed.</summary>
    private async Task LoadTabAsync(int index)
    {
        if (!_ready || !_dirty[index]) return;
        _dirty[index] = false;

        switch (index)
        {
            case 0: await LoadOverviewAsync(); break;
            case 1: await LoadToolsAsync(); break;
            case 2: await LoadReservationsAsync(); break;
            case 3: await LoadUsersAsync(); break;
            case 4: await LoadAuditAsync(); break;
        }
    }

    private async Task ReloadAsync()
    {
        MarkAllDirty();
        await LoadTabAsync(_currentTab);
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await GuardAsync(async () =>
    {
        await _hub.Compliance.RunPolicyScanAsync(Admin);
        await ReloadAsync();
        ShowBanner("Refreshed.", true);
    });

    private async Task LoadOverviewAsync()
    {
        var stats = await _hub.Inventory.GetStatsAsync();
        statTotal.Text = stats.Total.ToString();
        statAvailable.Text = stats.Available.ToString();
        statBorrowed.Text = stats.Borrowed.ToString();
        statOverdue.Text = stats.Overdue.ToString();
        statQuarantine.Text = stats.Quarantined.ToString();
        statLost.Text = stats.Lost.ToString();
        statCalibration.Text = stats.CalibrationAlerts.ToString();
        statReservations.Text = stats.ActiveReservations.ToString();

        var attention = await _hub.Inventory.GetAttentionItemsAsync();
        BindableLayout.SetItemsSource(lstAttention, attention);
        lblAttentionEmpty.IsVisible = attention.Count == 0;
    }

    private async Task LoadToolsAsync()
    {
        var category = pckFilterCategory.SelectedIndex > 0 ? pckFilterCategory.SelectedItem as string : null;
        var status = pckFilterStatus.SelectedIndex > 0 ? pckFilterStatus.SelectedItem as string : null;
        var sort = pckSort.SelectedItem as string ?? "Name";

        var items = await _hub.Inventory.GetItemsAsync(sbTools.Text, category, status, sort);
        BindableLayout.SetItemsSource(lstTools, items);
        lblToolsEmpty.IsVisible = items.Count == 0;
        if (_selected != null) await LoadHistoryAsync(_selected.ItemId);
    }

    private async Task LoadReservationsAsync()
    {
        var list = await _hub.Circulation.GetReservationsAsync(Admin);
        BindableLayout.SetItemsSource(lstReservations, list);
        lblReservationsEmpty.IsVisible = list.Count == 0;
    }

    private async Task LoadUsersAsync() =>
        BindableLayout.SetItemsSource(lstUsers, await _hub.Auth.GetUsersAsync(Admin));

    private async Task LoadAuditAsync()
    {
        var rows = await _hub.Compliance.GetAuditAsync(null, sbAudit.Text, 200);
        BindableLayout.SetItemsSource(lstAudit, rows);
        lblAuditEmpty.IsVisible = rows.Count == 0;
    }

    private async Task LoadHistoryAsync(string itemId)
    {
        var rows = await _hub.Compliance.GetAuditAsync(itemId, null, 100);
        lblHistoryTitle.Text = $"History for {itemId}";
        BindableLayout.SetItemsSource(lstHistory, rows);
        pnlHistory.IsVisible = true;
    }

    private async void OnFilterChanged(object? sender, EventArgs e)
    {
        if (!_ready) return;
        _dirty[1] = true;
        await GuardAsync(() => LoadTabAsync(1));
    }

    private async void OnSearchTextChanged(object? sender, EventArgs e)
    {
        if (!_ready) return;
        await GuardAsync(() => _toolSearch.RunAsync(() => { _dirty[1] = true; return LoadTabAsync(1); }));
    }

    private async void OnAuditSearchChanged(object? sender, EventArgs e)
    {
        if (!_ready) return;
        await GuardAsync(() => _auditSearch.RunAsync(() => { _dirty[4] = true; return LoadTabAsync(4); }));
    }

    // ------------------------------------------------------------------
    // Header actions and exports
    // ------------------------------------------------------------------
    private async void OnScanClicked(object? sender, EventArgs e) => await GuardAsync(async () =>
    {
        var scan = await _hub.Compliance.RunPolicyScanAsync(Admin);
        await ReloadAsync();

        var overdueNow = await _hub.Inventory.GetItemsAsync(status: ToolStatus.Overdue);
        ShowBanner($"Scan complete. {scan.NewlyOverdue} newly overdue, {scan.NewlyLocked} locked for expired calibration, " +
                   $"{scan.ExpiredReservations} reservations expired. {overdueNow.Count} tool(s) are overdue in total.", true);

        if (overdueNow.Count > 0)
        {
            var lines = string.Join("\n", overdueNow.Select(i => $"{i.ItemId}  {i.Name}: {i.CurrentBorrowerName} (due {i.DueDate:dd MMM HH:mm})"));
            await DisplayAlertAsync("Overdue tools", lines, "OK");
        }
    });

    private async Task ExportAsync(CsvReport report)
    {
        var (result, fileName, csv) = await _hub.Compliance.BuildReportAsync(Admin, report);
        if (!result.Ok) { ShowResult(result); return; }
        var (ok, message) = await ExportService.SaveCsvAsync(fileName, csv);
        ShowBanner(message, ok);
        _dirty[4] = true;
    }

    private async void OnExportReportClicked(object? sender, EventArgs e) => await GuardAsync(() => ExportAsync(new CustodyReport()));
    private async void OnExportLedgerClicked(object? sender, EventArgs e) => await GuardAsync(() => ExportAsync(new AuditLedgerReport()));
    private async void OnExportInventoryClicked(object? sender, EventArgs e) => await GuardAsync(() => ExportAsync(new InventoryReport()));

    private async void OnVerifyLedgerClicked(object? sender, EventArgs e) => await GuardAsync(async () =>
    {
        var check = await _hub.Compliance.VerifyLedgerAsync();
        lblLedger.IsVisible = true;
        lblLedger.TextColor = check.Intact ? Good : Bad;
        lblLedger.Text = check.Intact
            ? $"Ledger intact: all {check.Entries} entries match their hash chain."
            : $"Ledger TAMPERED: entry #{check.FirstBrokenId} (and everything after it) does not match its hash chain.";
    });

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        _session.Clear();
        await Navigation.PopToRootAsync();
    }

    // ------------------------------------------------------------------
    // Tools: select, add, edit, lock, release, delete
    // ------------------------------------------------------------------
    private async void OnToolTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not Item item) return;
        await GuardAsync(async () =>
        {
            SelectTool(item);
            _currentTab = 1;
            ApplyTabStyles(1);
            await LoadTabAsync(1);
            await LoadHistoryAsync(item.ItemId);
        });
    }

    private void SelectTool(Item item)
    {
        _selected = item;
        lblFormTitle.Text = $"Edit {item.ItemId}";
        btnSave.Text = "Save changes";
        txtId.Text = item.ItemId;
        txtId.IsEnabled = false;
        txtName.Text = item.Name;
        txtDesc.Text = item.Description;
        txtSerial.Text = item.SerialNumber;
        var cat = Array.IndexOf(ToolCategories.All, item.Category);
        pckCategory.SelectedIndex = cat >= 0 ? cat : ToolCategories.All.Length - 1;
        swCalibration.IsToggled = item.RequiresCalibration;
        dpCalibration.Date = item.NextCalibrationDate;
    }

    private void ClearForm()
    {
        _selected = null;
        lblFormTitle.Text = "Register a tool";
        btnSave.Text = "Add tool";
        txtId.Text = string.Empty;
        txtId.IsEnabled = true;
        txtName.Text = string.Empty;
        txtDesc.Text = string.Empty;
        txtSerial.Text = string.Empty;
        pckCategory.SelectedIndex = ToolCategories.All.Length - 1;
        swCalibration.IsToggled = true;
        dpCalibration.Date = DateTime.Today.AddMonths(6);
        pnlHistory.IsVisible = false;
    }

    private void OnClearFormClicked(object? sender, EventArgs e) => ClearForm();

    private Item ReadForm() => new()
    {
        ItemId = txtId.Text?.Trim() ?? string.Empty,
        Name = txtName.Text?.Trim() ?? string.Empty,
        Description = txtDesc.Text?.Trim() ?? string.Empty,
        SerialNumber = txtSerial.Text?.Trim() ?? string.Empty,
        Category = pckCategory.SelectedItem as string ?? "Other",
        RequiresCalibration = swCalibration.IsToggled,
        NextCalibrationDate = dpCalibration.Date ?? DateTime.Today.AddMonths(6)
    };

    private async Task ReselectAsync(string itemId)
    {
        await ReloadAsync();
        var refreshed = await _hub.Inventory.GetItemAsync(itemId);
        if (refreshed != null) SelectTool(refreshed); else ClearForm();
    }

    private async void OnSaveToolClicked(object? sender, EventArgs e) => await GuardAsync(async () =>
    {
        var form = ReadForm();
        var adding = _selected == null;
        if (!adding) form.ItemId = _selected!.ItemId;

        var result = adding
            ? await _hub.Inventory.AddItemAsync(Admin, form)
            : await _hub.Inventory.UpdateItemAsync(Admin, form);
        ShowResult(result);
        if (!result.Ok) return;

        if (adding) { await ReloadAsync(); ClearForm(); }
        else await ReselectAsync(form.ItemId);
    });

    private async Task RunOnSelectedAsync(Func<string, Task<OpResult>> action)
    {
        if (_selected == null)
        {
            ShowBanner("Select a tool from the list first.", false);
            return;
        }
        var id = _selected.ItemId;
        ShowResult(await action(id));
        await ReselectAsync(id);
    }

    private async void OnLockClicked(object? sender, EventArgs e) =>
        await GuardAsync(() => RunOnSelectedAsync(id => _hub.Inventory.LockForCalibrationAsync(Admin, id)));

    private async void OnReleaseClicked(object? sender, EventArgs e) =>
        await GuardAsync(() => RunOnSelectedAsync(id => _hub.Inventory.ReleaseItemAsync(Admin, id)));

    private async void OnDeleteClicked(object? sender, EventArgs e) => await GuardAsync(async () =>
    {
        if (_selected == null)
        {
            ShowBanner("Select a tool from the list first.", false);
            return;
        }
        var ok = await DisplayAlertAsync("Delete tool", $"Delete {_selected.ItemId} ({_selected.Name})? Its past records stay in the audit ledger.", "Delete", "Keep");
        if (!ok) return;

        var result = await _hub.Inventory.DeleteItemAsync(Admin, _selected.ItemId);
        ShowResult(result);
        if (result.Ok) ClearForm();
        await ReloadAsync();
    });

    // ------------------------------------------------------------------
    // Reservations
    // ------------------------------------------------------------------
    private async void OnCancelReservationClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not Reservation r) return;
        await GuardAsync(async () =>
        {
            var ok = await DisplayAlertAsync("Cancel reservation", $"Cancel {r.ItemId} for {r.DateText} ({r.UserName})?", "Cancel it", "Keep");
            if (!ok) return;
            ShowResult(await _hub.Circulation.CancelReservationAsync(Admin, r.Id));
            await ReloadAsync();
        });
    }

    // ------------------------------------------------------------------
    // Users
    // ------------------------------------------------------------------
    private async void OnAddUserClicked(object? sender, EventArgs e) => await GuardAsync(async () =>
    {
        var role = pckNewRole.SelectedIndex == 1 ? UserRole.Admin : UserRole.User;
        var title = pckNewTitle.SelectedItem as string ?? "Journeyman";
        var result = await _hub.Auth.AddUserAsync(Admin, txtNewUsername.Text ?? "", txtNewFullName.Text ?? "", role, title,
            txtNewInitials.Text, txtNewPassword.Text ?? "", txtNewPin.Text ?? "");
        ShowResult(result);
        if (!result.Ok) return;

        txtNewUsername.Text = txtNewFullName.Text = txtNewInitials.Text = txtNewPassword.Text = txtNewPin.Text = string.Empty;
        await ReloadAsync();
    });

    private static User? UserFrom(object? sender) => (sender as BindableObject)?.BindingContext as User;

    private async void OnResetPasswordClicked(object? sender, EventArgs e)
    {
        var u = UserFrom(sender);
        if (u == null) return;
        await GuardAsync(async () =>
        {
            var pw = await DisplayPromptAsync("Reset password", $"New password for {u.FullName} (6 or more characters). It is visible while you type.",
                "Save", "Cancel", maxLength: 64);
            if (pw == null) return;
            ShowResult(await _hub.Auth.ResetPasswordAsync(Admin, u.Id, pw));
            _dirty[4] = true;
        });
    }

    private async void OnSetPinClicked(object? sender, EventArgs e)
    {
        var u = UserFrom(sender);
        if (u == null) return;
        await GuardAsync(async () =>
        {
            var pin = await DisplayPromptAsync("Reset handover PIN", $"New 4-digit PIN for {u.FullName}.",
                "Save", "Cancel", maxLength: 4, keyboard: Keyboard.Numeric);
            if (pin == null) return;
            ShowResult(await _hub.Auth.ResetPinAsync(Admin, u.Id, pin));
            _dirty[4] = true;
        });
    }

    private async void OnResetRiskClicked(object? sender, EventArgs e)
    {
        var u = UserFrom(sender);
        if (u == null) return;
        await GuardAsync(async () =>
        {
            var ok = await DisplayAlertAsync("Clear risk score", $"Reset {u.FullName}'s score from {u.RiskScore} to 0?", "Clear", "Keep");
            if (!ok) return;
            ShowResult(await _hub.Auth.ResetRiskScoreAsync(Admin, u.Id));
            await ReloadAsync();
        });
    }

    private async void OnToggleActiveClicked(object? sender, EventArgs e)
    {
        var u = UserFrom(sender);
        if (u == null) return;
        await GuardAsync(async () =>
        {
            if (u.IsActive)
            {
                var ok = await DisplayAlertAsync("Deactivate user", $"{u.FullName} will no longer be able to sign in.", "Deactivate", "Keep active");
                if (!ok) return;
            }
            ShowResult(await _hub.Auth.SetActiveAsync(Admin, u.Id, !u.IsActive));
            await ReloadAsync();
        });
    }
}
