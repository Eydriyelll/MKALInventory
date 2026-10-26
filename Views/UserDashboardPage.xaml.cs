using MkalInventoryApp.Models;
using MkalInventoryApp.Services;

namespace MkalInventoryApp.Views;

public partial class UserDashboardPage : ContentPage
{
    private static readonly Color Orange = Color.FromArgb("#FF6F00");
    private static readonly Color Ink = Color.FromArgb("#1F2933");
    private static readonly Color Good = Color.FromArgb("#2E7D32");
    private static readonly Color Bad = Color.FromArgb("#C62828");

    private readonly ServiceHub _hub;
    private readonly SessionService _session;
    private readonly SignatureDrawable _signature = new();
    private readonly Debouncer _search = new();
    private readonly Button[] _tabs;
    private readonly View[] _panels;
    private readonly bool[] _dirty = { true, true, true, true, true };

    private int _currentTab;
    private bool _ready;
    private int _bannerVersion;
    private string _photoPath = string.Empty;

    private User Me => _session.CurrentUser!;

    public UserDashboardPage(ServiceHub hub, SessionService session)
    {
        InitializeComponent();
        _hub = hub;
        _session = session;
        NavigationPage.SetHasBackButton(this, false);

        _tabs = new[] { tab0, tab1, tab2, tab3, tab4 };
        _panels = new View[] { pnl0, pnl1, pnl2, pnl3, pnl4 };

        pckFilterCategory.ItemsSource = new[] { "All categories" }.Concat(ToolCategories.All).ToList();
        pckFilterCategory.SelectedIndex = 0;
        pckFilterStatus.ItemsSource = new[] { "All statuses" }.Concat(ToolStatus.All).ToList();
        pckFilterStatus.SelectedIndex = 0;
        pckReturnWindow.ItemsSource = ReturnWindow.All;
        pckReturnWindow.SelectedIndex = 0;
        pckCondition.ItemsSource = ReturnCondition.All;
        pckCondition.SelectedIndex = 0;
        dpReserve.MinimumDate = DateTime.Today;
        dpReserve.Date = DateTime.Today.AddDays(1);

        sigCanvas.Drawable = _signature;
        sigCanvas.StartInteraction += (_, e) => { _signature.BeginStroke(e.Touches[0]); sigCanvas.Invalidate(); };
        sigCanvas.DragInteraction += (_, e) => { _signature.AddPoint(e.Touches[0]); sigCanvas.Invalidate(); };
        sigCanvas.EndInteraction += (_, e) => sigCanvas.Invalidate();

        lblWho.Text = $"Signed in as {Me.FullName}, {Me.JobTitle}";
        ApplyTabStyles(0);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _ready = true;
        await GuardAsync(async () =>
        {
            await _hub.Compliance.RunPolicyScanAsync();
            MarkAllDirty();
            await LoadTabAsync(_currentTab);
        });
    }

    // ------------------------------------------------------------------
    // Feedback, tabs, loading
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

    private async Task GuardAsync(Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex)
        {
            CrashLog.Write("User", ex);
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

    private async Task GoToTabAsync(int index)
    {
        _currentTab = index;
        ApplyTabStyles(index);
        await LoadTabAsync(index);
    }

    private async Task ReloadAsync()
    {
        MarkAllDirty();
        await LoadTabAsync(_currentTab);
    }

    private async Task LoadTabAsync(int index)
    {
        if (!_ready || !_dirty[index]) return;
        _dirty[index] = false;

        switch (index)
        {
            case 0: await LoadToolsAsync(); break;
            case 2: await LoadMineAsync(); break;
            case 3: await LoadReservationsAsync(); break;
            case 4: await LoadAccountAsync(); break;
        }
    }

    private async Task LoadToolsAsync()
    {
        var category = pckFilterCategory.SelectedIndex > 0 ? pckFilterCategory.SelectedItem as string : null;
        var status = pckFilterStatus.SelectedIndex > 0 ? pckFilterStatus.SelectedItem as string : null;
        var items = await _hub.Inventory.GetItemsAsync(sbTools.Text, category, status, "Name");
        BindableLayout.SetItemsSource(lstTools, items);
        lblToolsEmpty.IsVisible = items.Count == 0;
    }

    private async Task LoadMineAsync()
    {
        var mine = await _hub.Inventory.GetHeldItemsAsync(Me.Id);
        BindableLayout.SetItemsSource(lstMine, mine);
        lblMineEmpty.IsVisible = mine.Count == 0;
    }

    private async Task LoadReservationsAsync()
    {
        var tools = (await _hub.Inventory.GetItemsAsync()).Where(i => i.Status != ToolStatus.Lost).ToList();
        pckReserveTool.ItemDisplayBinding = new Binding(nameof(Item.DisplayLabel));
        pckReserveTool.ItemsSource = tools;

        var res = await _hub.Circulation.GetReservationsAsync(Me);
        BindableLayout.SetItemsSource(lstReservations, res);
        lblReservationsEmpty.IsVisible = res.Count == 0;
    }

    private async Task LoadAccountAsync()
    {
        var fresh = await _hub.Auth.GetUserAsync(Me.Id) ?? Me;
        lblAccName.Text = fresh.FullName;
        lblAccUsername.Text = $"{fresh.Username}, {fresh.TitleText}";
        lblAccInitials.Text = $"Registered sign-off initials: {fresh.Initials}";
        lblAccRisk.Text = fresh.RiskText;
        lblAccRisk.TextColor = fresh.RiskColor;
    }

    private async void OnFilterChanged(object? sender, EventArgs e)
    {
        if (!_ready) return;
        _dirty[0] = true;
        await GuardAsync(() => LoadTabAsync(0));
    }

    private async void OnSearchTextChanged(object? sender, EventArgs e)
    {
        if (!_ready) return;
        await GuardAsync(() => _search.RunAsync(() => { _dirty[0] = true; return LoadTabAsync(0); }));
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await GuardAsync(async () =>
    {
        await _hub.Compliance.RunPolicyScanAsync();
        await ReloadAsync();
        ShowBanner("Catalog refreshed.", true);
    });

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        _session.Clear();
        await Navigation.PopToRootAsync();
    }

    // ------------------------------------------------------------------
    // Tapping a tool: checkout if available, return if it is mine
    // ------------------------------------------------------------------
    private async void OnToolTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not Item item) return;
        await GuardAsync(async () =>
        {
            if (item.CurrentBorrowerId == Me.Id)
            {
                txtReturnId.Text = item.ItemId;
                _dirty[2] = true;
                await GoToTabAsync(2);
                ShowBanner($"Ready to return {item.ItemId}. Choose the condition and tap Check in / return.", true);
            }
            else if (item.Status == ToolStatus.Available)
            {
                txtBorrowId.Text = item.ItemId;
                await GoToTabAsync(1);
            }
            else
            {
                ShowBanner(item.Status switch
                {
                    ToolStatus.Borrowed or ToolStatus.Overdue => $"{item.ItemId} is with {item.CurrentBorrowerName}.",
                    ToolStatus.UnderMaintenance => $"{item.ItemId} is Under Maintenance and cannot be issued.",
                    ToolStatus.Damaged => $"{item.ItemId} is quarantined as Damaged.",
                    _ => $"{item.ItemId} is {item.Status}."
                }, false);
            }
        });
    }

    // ------------------------------------------------------------------
    // Check out
    // ------------------------------------------------------------------
    private void OnClearCanvasClicked(object? sender, EventArgs e)
    {
        _signature.Clear();
        sigCanvas.Invalidate();
    }

    private async void OnBorrowClicked(object? sender, EventArgs e) => await GuardAsync(async () =>
    {
        var window = pckReturnWindow.SelectedItem as string ?? ReturnWindow.EndOfShift;
        var result = await _hub.Circulation.BorrowAsync(Me, txtBorrowId.Text ?? "", txtPin.Text ?? "",
            txtInitials.Text ?? "", _signature.ToData(), window);
        ShowBanner(result.Message, result.Ok);

        txtPin.Text = string.Empty; // never leave a PIN sitting in the field
        if (!result.Ok) return;

        txtBorrowId.Text = string.Empty;
        txtInitials.Text = string.Empty;
        OnClearCanvasClicked(null, EventArgs.Empty);
        MarkAllDirty();
        await GoToTabAsync(2);
    });

    // ------------------------------------------------------------------
    // Return and inspection
    // ------------------------------------------------------------------
    private async Task<string?> SavePhotoAsync(FileResult? photo)
    {
        if (photo == null) return null;
        var dir = Path.Combine(FileSystem.AppDataDirectory, "inspection_photos");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{Path.GetFileName(photo.FileName)}");
        using var source = await photo.OpenReadAsync();
        using var target = File.Create(path);
        await source.CopyToAsync(target);
        return path;
    }

    private void AttachPhoto(string path)
    {
        _photoPath = path;
        txtPhotoPath.Text = path;
        imgPreview.Source = ImageSource.FromFile(path);
        imgPreview.IsVisible = true;
    }

    private void ClearPhoto()
    {
        _photoPath = string.Empty;
        txtPhotoPath.Text = string.Empty;
        imgPreview.Source = null;
        imgPreview.IsVisible = false;
    }

    private async void OnCapturePhotoClicked(object? sender, EventArgs e) => await GuardAsync(async () =>
    {
        if (!MediaPicker.Default.IsCaptureSupported)
        {
            ShowBanner("This device has no camera available to the app. Use Choose file instead.", false);
            return;
        }
        var path = await SavePhotoAsync(await MediaPicker.Default.CapturePhotoAsync());
        if (path != null) AttachPhoto(path);
    });

    private async void OnPickPhotoClicked(object? sender, EventArgs e) => await GuardAsync(async () =>
    {
#pragma warning disable CS0618 // PickPhotoAsync is flagged obsolete in favour of the multi-select API; one photo is all we need
        var path = await SavePhotoAsync(await MediaPicker.Default.PickPhotoAsync());
#pragma warning restore CS0618
        if (path != null) AttachPhoto(path);
    });

    private void OnRemovePhotoClicked(object? sender, EventArgs e) => ClearPhoto();

    private async void OnReturnClicked(object? sender, EventArgs e) => await GuardAsync(async () =>
    {
        var condition = pckCondition.SelectedItem as string ?? string.Empty;
        if (condition == ReturnCondition.Lost)
        {
            var ok = await DisplayAlertAsync("Report tool as lost",
                $"Reporting a lost tool adds {Policy.LostRiskPoints} points to your risk score. Continue?", "Report lost", "Cancel");
            if (!ok) return;
        }

        var result = await _hub.Circulation.ReturnAsync(Me, txtReturnId.Text ?? "", condition, txtNotes.Text ?? "", _photoPath);
        ShowBanner(result.Message, result.Ok);
        if (!result.Ok) return;

        txtReturnId.Text = string.Empty;
        txtNotes.Text = string.Empty;
        pckCondition.SelectedIndex = 0;
        ClearPhoto();
        await ReloadAsync();
    });

    // ------------------------------------------------------------------
    // Reservations
    // ------------------------------------------------------------------
    private async void OnReserveClicked(object? sender, EventArgs e) => await GuardAsync(async () =>
    {
        var tool = pckReserveTool.SelectedItem as Item;
        var result = await _hub.Circulation.ReserveAsync(Me, tool?.ItemId ?? "", dpReserve.Date ?? DateTime.Today.AddDays(1), txtProject.Text ?? "");
        ShowBanner(result.Message, result.Ok);
        if (!result.Ok) return;

        txtProject.Text = string.Empty;
        await ReloadAsync();
    });

    private async void OnCancelReservationClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not Reservation r) return;
        await GuardAsync(async () =>
        {
            var ok = await DisplayAlertAsync("Cancel reservation", $"Cancel {r.ItemId} for {r.DateText}?", "Cancel it", "Keep");
            if (!ok) return;
            var result = await _hub.Circulation.CancelReservationAsync(Me, r.Id);
            ShowBanner(result.Message, result.Ok);
            await ReloadAsync();
        });
    }

    // ------------------------------------------------------------------
    // Account
    // ------------------------------------------------------------------
    private async void OnChangePinClicked(object? sender, EventArgs e) => await GuardAsync(async () =>
    {
        if ((txtNewPin.Text ?? "") != (txtConfirmPin.Text ?? ""))
        {
            ShowBanner("The new PIN and its confirmation do not match.", false);
            return;
        }
        var result = await _hub.Auth.ChangeOwnPinAsync(Me, txtCurPin.Text ?? "", txtNewPin.Text ?? "");
        ShowBanner(result.Message, result.Ok);
        if (result.Ok) txtCurPin.Text = txtNewPin.Text = txtConfirmPin.Text = string.Empty;
    });
}
