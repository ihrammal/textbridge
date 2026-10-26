using System.Diagnostics;

namespace AccessibleOCRStudio.Services;

public sealed class ImagePreprocessingService
{
    public async Task<(string Path, bool Temporary)> PrepareAsync(string input, AppSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!settings.Deskew && !settings.Grayscale) return (input, false);
        var bundled = Path.Combine(AppContext.BaseDirectory, "image", "magick.exe");
        if (!File.Exists(bundled)) return (input, false); // preprocessing is optional until ImageMagick is bundled

        var output = Path.Combine(Path.GetTempPath(), "AccessibleOCRStudio", $"prep-{Guid.NewGuid():N}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var psi = new ProcessStartInfo(bundled) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        psi.ArgumentList.Add(input);
        if (settings.AutoRotate) psi.ArgumentList.Add("-auto-orient");
        if (settings.Grayscale) { psi.ArgumentList.Add("-colorspace"); psi.ArgumentList.Add("Gray"); }
        if (settings.Deskew) { psi.ArgumentList.Add("-deskew"); psi.ArgumentList.Add("40%"); }
        psi.ArgumentList.Add(output);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Unable to start image preprocessing.");
        using var stop = cancellationToken.Register(() => { try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { } });
        string err;
        try { err = await p.StandardError.ReadToEndAsync(cancellationToken); await p.WaitForExitAsync(cancellationToken); }
        catch { try { File.Delete(output); } catch { } throw; }
        if (p.ExitCode != 0) { try { File.Delete(output); } catch { } return (input, false); }
        return (output, true);
    }
}
