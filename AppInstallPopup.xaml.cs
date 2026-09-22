using ExcelDriversSync.Services;
using Microsoft.Maui.Controls.Shapes;

namespace ExcelDriversSync;

public partial class AppInstallPopup : ContentPage
{
    private List<AppPackageInfo> _apps;
    private AppInstallService _installService;

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

    private async void ShowLoading(string message)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            LoadingText.Text = message;
            LoadingOverlay.IsVisible = true;
            LoadingSpinner.IsRunning = true;
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

    private Border BuildItem(AppPackageInfo app)
    {
        // Capture app in a local variable to avoid closure bug
        var localApp = app;

        var iconChar = app.IsInstalled ? "✓" : "○";
        var iconColor = app.IsInstalled ? Color.FromArgb("#4caf50") : Color.FromArgb("#ff5722");
        var statusText = BuildStatusText(app);
        var btnText = app.IsInstalled ? "↻ Check for updates" : "⬇ Download";
        var btnColor = app.IsInstalled ? Color.FromArgb("#ff9800") : Color.FromArgb("#4caf50");

        // Status icon
        var statusIcon = new Label
        {
            Text = iconChar,
            FontSize = 24,
            TextColor = iconColor,
            WidthRequest = 36,
            HeightRequest = 36,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start
        };

        // App info
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
            Text = statusText,
            FontSize = 12,
            TextColor = iconColor,
            FontAttributes = FontAttributes.Bold
        };
        infoLayout.Add(nameLabel);
        infoLayout.Add(statusLabel);

        // Install / update button
        var installBtn = new Button
        {
            Text = btnText,
            BackgroundColor = btnColor,
            TextColor = Colors.White,
            CornerRadius = 8,
            FontAttributes = FontAttributes.Bold,
            FontSize = 13,
            HorizontalOptions = LayoutOptions.End
        };

        installBtn.Clicked += async (s, e) =>
        {
            Console.WriteLine($"[Popup] Install/update button clicked for: {localApp.Name}");
            installBtn.IsEnabled = false;
            ShowLoading(localApp.IsInstalled ? $"Checking {localApp.Name} for updates..." : $"Downloading {localApp.Name}...");
            try
            {
                var result = await _installService.InstallAppAsync(localApp);
                switch (result)
                {
                    case AppInstallResult.Installing:
                        localApp.IsInstalled = true;
                        statusLabel.Text = BuildStatusText(localApp);
                        statusLabel.TextColor = Color.FromArgb("#4caf50");
                        installBtn.Text = "✓ Installed";
                        installBtn.BackgroundColor = Color.FromArgb("#e0e0e0");
                        installBtn.IsEnabled = false;
                        break;
                    case AppInstallResult.AlreadyUpToDate:
                        statusLabel.Text = BuildStatusText(localApp);
                        statusLabel.TextColor = Color.FromArgb("#4caf50");
                        installBtn.Text = "↻ Check for updates";
                        installBtn.IsEnabled = true;
                        break;
                    case AppInstallResult.Failed:
                        installBtn.IsEnabled = true;
                        break;
                }
            }
            finally
            {
                HideLoading();
            }
        };

        // Card layout
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
            BackgroundColor = app.IsInstalled ? Color.FromArgb("#e8f5e9") : Colors.White,
            Padding = 0
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
