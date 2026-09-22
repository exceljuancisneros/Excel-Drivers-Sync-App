using ExcelDriversSync.Services;
using Microsoft.Maui.Controls.Shapes;

namespace ExcelDriversSync;

public partial class AppInstallPopup : ContentPage
{
    private readonly record struct Row(AppPackageInfo App, Border Card, Label StatusIcon, Label StatusLabel, Button ActionBtn);

    private List<AppPackageInfo> _apps;
    private AppInstallService _installService;
    private readonly List<Row> _rows = new();

    public AppInstallPopup(List<AppPackageInfo> apps, AppInstallService installService)
    {
        InitializeComponent();
        _apps = apps ?? new List<AppPackageInfo>();
        _installService = installService;

        Console.WriteLine($"[Popup] Loading {_apps.Count} apps...");

        foreach (var app in _apps)
        {
            var item = BuildItem(app);
            AppListContainer.Add(item);
            Console.WriteLine($"[Popup] Added: {app.Name} - Installed: {app.IsInstalled}");
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Re-check reality every time this page becomes visible - in particular when
        // returning from Android's own installer, which the app has no result callback
        // for (it's launched as a plain ACTION_VIEW intent, not startActivityForResult).
        if (_rows.Count == 0) return;

        _installService.InvalidateInstalledPackagesCache();
        foreach (var row in _rows)
        {
            var status = _installService.GetInstallStatus(row.App.PackageName);
            row.App.IsInstalled = status.IsInstalled;
            row.App.InstalledVersionCode = status.VersionCode;
            row.App.InstalledVersionName = status.VersionName;
            ApplyStatus(row);
        }
    }

    private async void ShowLoading(string message)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            LoadingText.Text = message;
            LoadingOverlay.IsVisible = true;
            LoadingSpinner.IsVisible = true;
            LoadingSpinner.IsRunning = true;
            DownloadProgressBar.IsVisible = false;
            DownloadProgressBar.Progress = 0;
        });
    }

    /// <summary>Progress&lt;T&gt;'s callback runs on the thread that constructed it, so as long as
    /// this is built from a UI-thread event handler (it always is here), no MainThread hop is needed.</summary>
    private IProgress<(long BytesRead, long? TotalBytes)> CreateDownloadProgress(string appName)
    {
        return new Progress<(long BytesRead, long? TotalBytes)>(p =>
        {
            LoadingSpinner.IsVisible = false;
            if (p.TotalBytes is > 0)
            {
                var percent = (int)(p.BytesRead * 100 / p.TotalBytes.Value);
                DownloadProgressBar.IsVisible = true;
                DownloadProgressBar.Progress = percent / 100.0;
                LoadingText.Text = $"Downloading {appName}... {percent}%";
            }
            else
            {
                DownloadProgressBar.IsVisible = false;
                LoadingSpinner.IsVisible = true;
                LoadingText.Text = $"Downloading {appName}... {p.BytesRead / (1024 * 1024)} MB";
            }
        });
    }

    private async void HideLoading()
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            LoadingOverlay.IsVisible = false;
            LoadingSpinner.IsRunning = false;
        });
    }

    private static string BuildStatusText(AppPackageInfo app)
    {
        if (!app.IsInstalled) return "Not installed";
        var version = app.InstalledVersionName ?? app.InstalledVersionCode?.ToString();
        return version != null ? $"Installed (v{version})" : "Installed";
    }

    private static void ApplyStatus(Row row)
    {
        var app = row.App;
        var color = app.IsInstalled ? Color.FromArgb("#4caf50") : Color.FromArgb("#ff5722");

        row.StatusIcon.Text = app.IsInstalled ? "✓" : "○";
        row.StatusIcon.TextColor = color;
        row.StatusLabel.Text = BuildStatusText(app);
        row.StatusLabel.TextColor = color;
        row.ActionBtn.Text = app.IsInstalled ? "↻ Check for updates" : "⬇ Download";
        row.ActionBtn.BackgroundColor = app.IsInstalled ? Color.FromArgb("#ff9800") : Color.FromArgb("#4caf50");
        row.ActionBtn.IsEnabled = true;
        row.Card.BackgroundColor = app.IsInstalled ? Color.FromArgb("#e8f5e9") : Colors.White;
    }

    private Border BuildItem(AppPackageInfo app)
    {
        // Capture app in a local variable to avoid closure bug
        var localApp = app;

        var statusIcon = new Label
        {
            FontSize = 24,
            WidthRequest = 36,
            HeightRequest = 36,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start
        };

        var infoLayout = new VerticalStackLayout { Spacing = 2, HorizontalOptions = LayoutOptions.Start };
        var nameLabel = new Label
        {
            Text = app.Name,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.Black
        };
        var statusLabel = new Label
        {
            FontSize = 12,
            FontAttributes = FontAttributes.Bold
        };
        infoLayout.Add(nameLabel);
        infoLayout.Add(statusLabel);

        var installBtn = new Button
        {
            TextColor = Colors.White,
            CornerRadius = 8,
            FontAttributes = FontAttributes.Bold,
            FontSize = 13,
            HorizontalOptions = LayoutOptions.End
        };

        var cardLayout = new VerticalStackLayout { Spacing = 10, Padding = 12 };
        var rowLayout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = 36 },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = 100 }
            }
        };
        rowLayout.Add(statusIcon, 0, 0);
        rowLayout.Add(infoLayout, 1, 0);
        rowLayout.Add(installBtn, 2, 0);
        cardLayout.Add(rowLayout);

        var border = new Border
        {
            Content = cardLayout,
            Stroke = Color.FromArgb("#e0e0e0"),
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            StrokeThickness = 1,
            Padding = 0
        };

        var row = new Row(localApp, border, statusIcon, statusLabel, installBtn);
        _rows.Add(row);
        ApplyStatus(row);

        installBtn.Clicked += async (s, e) =>
        {
            Console.WriteLine($"[Popup] Install/update button clicked for: {localApp.Name}");
            installBtn.IsEnabled = false;
            ShowLoading(localApp.IsInstalled ? $"Checking {localApp.Name} for updates..." : $"Downloading {localApp.Name}...");
            try
            {
                var progress = CreateDownloadProgress(localApp.Name);
                var result = await _installService.InstallAppAsync(localApp, progress);
                switch (result)
                {
                    case AppInstallResult.AlreadyUpToDate:
                        ApplyStatus(row);
                        break;
                    case AppInstallResult.Failed:
                        installBtn.IsEnabled = true;
                        break;
                    case AppInstallResult.Installing:
                        // The installer intent launched, but Android hasn't actually
                        // installed anything yet (and the user can still cancel it) -
                        // leave the button disabled and let OnAppearing reconcile the
                        // real status once we regain focus from the system installer.
                        break;
                }
            }
            finally
            {
                HideLoading();
            }
        };

        Console.WriteLine($"[Popup] Built item for: {app.Name}");
        return border;
    }

    private void OnCancelClicked(object sender, EventArgs e)
    {
        Console.WriteLine("[Popup] Cancel clicked");
        Navigation.PopModalAsync();
    }
}
