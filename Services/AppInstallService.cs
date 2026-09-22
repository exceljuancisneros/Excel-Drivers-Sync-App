using Android.Content;
using Android.Content.PM;
using Android.OS;
using Java.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using AndroidX.Core.Content;
using System.Diagnostics;

namespace ExcelDriversSync.Services;

public enum AppInstallResult
{
    Installing,
    AlreadyUpToDate,
    Failed
}

public class AppInstallService
{
    // Driver-app binaries (Samsara Driver, ePOD) live on a fixed "driver-apps" tag, not
    // /releases/latest - that endpoint is reserved for the sync tool's own app releases.
    private const string DriverAppsReleaseUrl = "https://api.github.com/repos/exceljuancisneros/Excel-Drivers-Sync-App/releases/tags/driver-apps";

    private readonly string _apkFolderPath;
    private readonly HttpClient _httpClient;

    private HashSet<string>? _installedPackagesCache;
    private bool _cacheLoaded = false;

    public AppInstallService()
    {
        _apkFolderPath = Android.App.Application.Context.FilesDir.AbsolutePath;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ExcelDriversSync");
    }

    /// <summary>Drops the installed-packages cache so the next check reflects reality (e.g. after returning from the system installer).</summary>
    public void InvalidateInstalledPackagesCache()
    {
        _cacheLoaded = false;
        _installedPackagesCache = null;
    }

    /// <summary>Ground-truth install status for one package, straight from PackageManager (respects the cache above).</summary>
    public (bool IsInstalled, long? VersionCode, string? VersionName) GetInstallStatus(string packageName)
    {
        var isInstalled = GetInstalledPackages().Contains(packageName);
        var version = isInstalled ? GetInstalledVersion(packageName) : null;
        return (isInstalled, version?.VersionCode, version?.VersionName);
    }

    private HashSet<string> GetInstalledPackages()
    {
        if (_cacheLoaded && _installedPackagesCache != null)
            return _installedPackagesCache;

        try
        {
            var packageManager = Android.App.Application.Context.PackageManager;
            var packages = packageManager.GetInstalledPackages(0);

            Android.Util.Log.Info("IsAppInstalled", $"GetInstalledPackages returned {packages.Count} packages");

            var result = new HashSet<string>();
            foreach (var pkg in packages)
            {
                var packageName = pkg.PackageName;
                if (!string.IsNullOrEmpty(packageName))
                {
                    result.Add(packageName);
                }
            }

            Android.Util.Log.Info("IsAppInstalled", $"Loaded {result.Count} total packages");

            _installedPackagesCache = result;
            _cacheLoaded = true;
            return result;
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("IsAppInstalled", $"GetInstalledPackages FAILED: {ex.Message}");
            _installedPackagesCache = new HashSet<string>();
            _cacheLoaded = true;
            return _installedPackagesCache;
        }
    }

    /// <summary>Version of an installed app, or null if it isn't installed.</summary>
    private static (long VersionCode, string? VersionName)? GetInstalledVersion(string packageName)
    {
        try
        {
            var pm = Android.App.Application.Context.PackageManager!;
            var info = pm.GetPackageInfo(packageName, (PackageInfoFlags)0);
            var code = OperatingSystem.IsAndroidVersionAtLeast(28) ? info!.LongVersionCode : info!.VersionCode;
            return (code, info.VersionName);
        }
        catch (PackageManager.NameNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Reads the version out of a downloaded APK file without installing it.</summary>
    private static (long VersionCode, string? VersionName)? PeekApkVersion(string apkPath)
    {
        try
        {
            var pm = Android.App.Application.Context.PackageManager!;
            var info = pm.GetPackageArchiveInfo(apkPath, (PackageInfoFlags)0);
            if (info == null) return null;
            var code = OperatingSystem.IsAndroidVersionAtLeast(28) ? info.LongVersionCode : info.VersionCode;
            return (code, info.VersionName);
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("IsAppInstalled", $"PeekApkVersion FAILED: {ex.Message}");
            return null;
        }
    }

    public List<AppPackageInfo> GetAllApps()
    {
        var appsToCheck = new List<AppPackageInfo>
        {
            new AppPackageInfo
            {
                Name = "Samsara Driver",
                PackageName = "com.samsara.driver",
                ApkFilename = "SamsaraDriver.apk"
            },
            new AppPackageInfo
            {
                Name = "ePOD",
                PackageName = "com.example.michoacana",
                ApkFilename = "EPOD.apk"
            }
        };

        foreach (var app in appsToCheck)
        {
            var status = GetInstallStatus(app.PackageName);
            app.IsInstalled = status.IsInstalled;
            app.IsSelected = !app.IsInstalled;
            app.InstalledVersionCode = status.VersionCode;
            app.InstalledVersionName = status.VersionName;
        }

        return appsToCheck;
    }

    public async Task<AppInstallResult> InstallAppAsync(AppPackageInfo app, IProgress<(long BytesRead, long? TotalBytes)>? progress = null)
    {
        try
        {
            var apkPath = await DownloadDriverApkAsync(app, progress);

            if (string.IsNullOrEmpty(apkPath))
            {
                await ShowToastAsync($"Failed to download {app.Name}");
                return AppInstallResult.Failed;
            }

            var downloadedVersion = PeekApkVersion(apkPath);
            var installedVersion = GetInstalledVersion(app.PackageName);

            if (installedVersion.HasValue && downloadedVersion.HasValue
                && downloadedVersion.Value.VersionCode <= installedVersion.Value.VersionCode)
            {
                System.IO.File.Delete(apkPath);
                await ShowToastAsync($"{app.Name} is already up to date (v{installedVersion.Value.VersionName ?? installedVersion.Value.VersionCode.ToString()})");
                return AppInstallResult.AlreadyUpToDate;
            }

            var opened = await OpenInstallerAsync(apkPath);

            if (opened)
            {
                if (downloadedVersion.HasValue)
                {
                    app.InstalledVersionCode = downloadedVersion.Value.VersionCode;
                    app.InstalledVersionName = downloadedVersion.Value.VersionName;
                }
                await ShowToastAsync($"Opening installer for {app.Name}...");
                return AppInstallResult.Installing;
            }
            else
            {
                await ShowToastAsync($"Failed to open installer for {app.Name}");
                return AppInstallResult.Failed;
            }
        }
        catch (Exception ex)
        {
            await ShowToastAsync($"Error: {ex.Message}");
            return AppInstallResult.Failed;
        }
    }

    private async Task<string?> DownloadDriverApkAsync(AppPackageInfo app, IProgress<(long BytesRead, long? TotalBytes)>? progress = null)
    {
        try
        {
            var response = await _httpClient.GetAsync(DriverAppsReleaseUrl);

            if (!response.IsSuccessStatusCode)
            {
                await ShowToastAsync("Failed to get driver-apps release from GitHub");
                return null;
            }

            var releaseJson = await response.Content.ReadAsStringAsync();

            using var jsonDoc = JsonDocument.Parse(releaseJson);
            var root = jsonDoc.RootElement;

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    var downloadUrl = asset.GetProperty("browser_download_url").GetString();

                    if (name != null && name.Equals(app.ApkFilename, System.StringComparison.OrdinalIgnoreCase) && downloadUrl != null)
                    {
                        var apkPath = Path.Combine(_apkFolderPath, app.ApkFilename);

                        using var downloadResponse = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);

                        if (!downloadResponse.IsSuccessStatusCode)
                        {
                            await ShowToastAsync("Failed to download APK");
                            return null;
                        }

                        await StreamToFileAsync(downloadResponse, apkPath, progress);

                        return apkPath;
                    }
                }
            }

            await ShowToastAsync($"No {app.ApkFilename} found in the driver-apps release");
            return null;
        }
        catch (Exception ex)
        {
            await ShowToastAsync($"Download failed: {ex.Message}");
            return null;
        }
    }

    private static async Task StreamToFileAsync(HttpResponseMessage downloadResponse, string apkPath, IProgress<(long BytesRead, long? TotalBytes)>? progress)
    {
        var totalBytes = downloadResponse.Content.Headers.ContentLength;

        await using var contentStream = await downloadResponse.Content.ReadAsStreamAsync();
        await using var fileStream = new System.IO.FileStream(apkPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        var lastReportedPercent = -1;
        var lastReportedBytes = 0L;
        const long reportByteThreshold = 262_144; // 256 KB, used only when the server doesn't send Content-Length

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead);
            totalRead += bytesRead;

            if (progress == null) continue;

            if (totalBytes is > 0)
            {
                var percent = (int)(totalRead * 100 / totalBytes.Value);
                if (percent == lastReportedPercent) continue;
                lastReportedPercent = percent;
                progress.Report((totalRead, totalBytes));
            }
            else if (totalRead - lastReportedBytes >= reportByteThreshold)
            {
                lastReportedBytes = totalRead;
                progress.Report((totalRead, null));
            }
        }

        progress?.Report((totalRead, totalBytes));
    }

    private async Task<bool> OpenInstallerAsync(string apkPath)
    {
        try
        {
            var apkFile = new Java.IO.File(apkPath);

            if (!apkFile.Exists())
            {
                await ShowToastAsync("APK file not found");
                return false;
            }

            var authority = "com.excelware.driverssync.fileprovider";
            var context = Android.App.Application.Context;

            var contentUri = global::AndroidX.Core.Content.FileProvider.GetUriForFile(context, authority, apkFile);

            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(contentUri, "application/vnd.android.package-archive");
            intent.AddFlags(ActivityFlags.NewTask);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);

            context.StartActivity(intent);
            return true;
        }
        catch (Exception ex)
        {
            await ShowToastAsync($"Failed to open installer: {ex.Message}");
            return false;
        }
    }

    private async Task ShowToastAsync(string message)
    {
        try
        {
            var context = Android.App.Application.Context;
            var toast = Android.Widget.Toast.MakeText(context, message, Android.Widget.ToastLength.Long);
            toast.Show();
        }
        catch
        {
            // Silent fail
        }
    }
}

public class AppPackageInfo
{
    public string Name { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string? ApkFilename { get; set; }
    public bool IsInstalled { get; set; }
    public bool IsSelected { get; set; }
    public long? InstalledVersionCode { get; set; }
    public string? InstalledVersionName { get; set; }
}
