using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace AccessibleOCRStudio.Services;

public sealed record UpdateCheckResult(bool Configured, bool UpdateAvailable, string CurrentVersion, string? LatestVersion,
    string? ReleaseUrl, string Message, string? InstallerUrl = null, string? Sha256 = null);

public sealed class UpdateService
{
    private readonly HttpClient _http;
    public UpdateService() : this(new HttpClient { Timeout = TimeSpan.FromMinutes(15) }) { }
    internal UpdateService(HttpClient http) => _http = http;
    internal static bool IsHttps(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);

    internal static UpdateCheckResult Parse(string json, string currentVersion)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string? Read(JsonElement item, string key) => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var tag = Read(root, "tag_name") ?? Read(root, "version");
        var page = Read(root, "html_url");
        if (!Version.TryParse(tag?.Trim().TrimStart('v', 'V'), out var latest) || !Version.TryParse(currentVersion, out var current))
            throw new InvalidDataException("The update service returned an invalid version number.");
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True ||
            root.TryGetProperty("prerelease", out var preview) && preview.ValueKind == JsonValueKind.True || latest <= current)
            return new(true, false, currentVersion, latest.ToString(), page, "You are using the latest available version.");
        var installer = Read(root, "installerUrl");
        var digest = Read(root, "sha256");
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            var name = $"TextBridge-Setup-{latest}.exe";
            foreach (var asset in assets.EnumerateArray())
                if (Read(asset, "name") == name)
                { installer = Read(asset, "browser_download_url"); digest = Read(asset, "digest"); break; }
        }
        digest = digest?.Replace("sha256:", "", StringComparison.OrdinalIgnoreCase);
        if (!IsHttps(installer) || digest?.Length != 64 || !digest.All(Uri.IsHexDigit))
            throw new InvalidDataException("The update has no secure installer address or valid SHA-256 checksum. Nothing was installed.");
        return new(true, true, currentVersion, latest.ToString(), page, "An update is available.", installer, digest);
    }

    public async Task<UpdateCheckResult> CheckAsync(string? feedUrl, string currentVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(feedUrl))
            return new(false, false, currentVersion, null, null, "Updates are not configured. Enter a public update feed address in Settings > App settings > Updates.");
        try
        {
            if (!IsHttps(feedUrl)) throw new InvalidDataException("The update feed must use HTTPS.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            using var request = new HttpRequestMessage(HttpMethod.Get, feedUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TextBridge", currentVersion));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound &&
                new Uri(feedUrl).Host == "api.github.com" && new Uri(feedUrl).AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal))
                return new(true, false, currentVersion, null, null, UiLanguage.T("No stable update has been published yet. You can continue using your current version."));
            response.EnsureSuccessStatusCode();
            if (!IsHttps(response.RequestMessage?.RequestUri?.AbsoluteUri)) throw new InvalidDataException("Insecure update redirect.");
            return Parse(await response.Content.ReadAsStringAsync(timeout.Token), currentVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return new(true, false, currentVersion, null, null, UiLanguage.T("Unable to check for updates.") + " " + ex.Message);
        }
    }

    public async Task<string> DownloadAsync(UpdateCheckResult update, CancellationToken cancellationToken = default)
    {
        if (!update.UpdateAvailable || !IsHttps(update.InstallerUrl) || update.Sha256?.Length != 64 || !update.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid update package.");
        var directory = Path.Combine(Path.GetTempPath(), "TextBridge-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "TextBridge-Setup.exe");
        try
        {
            using var response = await _http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (!IsHttps(response.RequestMessage?.RequestUri?.AbsoluteUri)) throw new InvalidDataException("Insecure installer redirect.");
            const long maximumBytes = 1024L * 1024 * 1024;
            if (response.Content.Headers.ContentLength > maximumBytes) throw new InvalidDataException("Update exceeds the download limit.");
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += count;
                    if (total > maximumBytes) throw new InvalidDataException("Update exceeds the download limit.");
                    await file.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
            }
            await using var input = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
            if (!hash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update checksum did not match. Nothing was installed.");
            return path;
        }
        catch { Directory.Delete(directory, true); throw; }
    }
}
