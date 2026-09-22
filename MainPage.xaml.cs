using ExcelDriversSync.Services;

namespace ExcelDriversSync;

public partial class MainPage : ContentPage
{
    private bool _isSpinning = false;
    private CancellationTokenSource? _spinCts;
    private AppInstallService? _appInstallService;

    public MainPage()
    {
        InitializeComponent();
        _appInstallService = new AppInstallService();
    }

    private async void OnSyncClicked(object? sender, EventArgs e)
    {
        if (_isSpinning) return;
        
        _isSpinning = true;
        StartSpinAnimation();
        SyncButton.Opacity = 0.7f;

        ProgressArea.IsVisible = true;
        ProgressBar.Progress = 0;
        ProgressPercent.Text = "0%";
        ProgressLabel.Text = "Checking for updates...";

        try
        {
            // Show loading state
            ProgressArea.IsVisible = true;
            ProgressBar.Progress = 0;
            ProgressPercent.Text = "0%";
            ProgressLabel.Text = "Checking installed apps...";

            // Get actual app data (synchronous call)
            var allApps = _appInstallService!.GetAllApps();
            
            System.Diagnostics.Debug.WriteLine($"[SYNC] Found {allApps.Count} apps - Installed: {allApps.Count(a => a.IsInstalled)}, Missing: {allApps.Count(a => !a.IsInstalled)}");

            var now = DateTime.Now;
            LastSyncLabel.Text = $"Last sync: {now:MMM d, yyyy HH:mm}";
            ProgressArea.IsVisible = false;

            // Show popup
            await ShowAppSelectionPopup(allApps);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SYNC] ERROR: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[SYNC] Stack: {ex.StackTrace}");
            
            ProgressLabel.Text = "Sync failed!";
            ProgressLabel.TextColor = Color.FromArgb("#ff4444");
            ProgressPercent.Text = $"Error: {ex.Message}";
            ProgressArea.IsVisible = true;

            await DisplayAlert("Sync Error", ex.Message + "\n\n" + ex.GetType().Name, "OK");

            ProgressArea.IsVisible = false;
        }
        finally
        {
            _isSpinning = false;
            SyncButton.Rotation = 0;
            SyncButton.Opacity = 1f;
        }
    }

    private async Task ShowAppSelectionPopup(List<AppPackageInfo> allApps)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[POPUP] Creating popup with {allApps.Count} apps");
            var popupPage = new AppInstallPopup(allApps, _appInstallService!);
            System.Diagnostics.Debug.WriteLine("[POPUP] Popup created, showing...");
            await Navigation.PushModalAsync(popupPage);
            System.Diagnostics.Debug.WriteLine("[POPUP] Popup shown successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[POPUP] ERROR: {ex.Message}");
            await DisplayAlert("Popup Error", ex.Message, "OK");
        }
    }

    private void StartSpinAnimation()
    {
        if (_spinCts != null)
        {
            _spinCts.Cancel();
        }

        _spinCts = new CancellationTokenSource();
        var cts = _spinCts;

        Task.Run(async () =>
        {
            double rotation = 0;
            while (_isSpinning && !cts.Token.IsCancellationRequested)
            {
                await Task.Delay(16);
                if (!cts.Token.IsCancellationRequested)
                {
                    rotation += 6;
                    if (rotation >= 360)
                        rotation = 0;

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        SyncButton.Rotation = rotation;
                    });
                }
            }
        });
    }
}