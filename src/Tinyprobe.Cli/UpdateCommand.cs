using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Spectre.Console;

namespace Tinyprobe.Cli;

/// <summary>
/// <c>tinyprobe update</c>: replaces the running binary with the latest GitHub release,
/// after verifying its SHA-256 checksum.
/// </summary>
internal static class UpdateCommand
{
    private const string Repository = "kikutano/tinyprobe";

    // Platforms with a prebuilt binary in every release (see .github/workflows/release.yml).
    private static readonly string[] SupportedRids = ["win-x64", "linux-x64", "linux-arm64", "linux-musl-x64", "linux-musl-arm64", "osx-arm64"];

    public static async Task<int> RunAsync(IAnsiConsole console, string currentVersion, bool checkOnly, bool force)
    {
        var assetName = GetAssetName(CurrentOs(), RuntimeInformation.ProcessArchitecture);
        if (assetName is null)
        {
            console.MarkupLine($"[red]No prebuilt binary for this platform ({Markup.Escape(RuntimeInformation.RuntimeIdentifier)}).[/] Build from source: https://github.com/{Repository}");
            return 1;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"tinyprobe/{currentVersion}");

        string latestTag;
        try
        {
            latestTag = await GetLatestTagAsync(http);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            console.MarkupLine($"[red]Cannot check for updates:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        var latestVersion = latestTag.TrimStart('v');
        var isDevBuild = !TryParseVersion(currentVersion, out _, out var currentIsPrerelease) || currentIsPrerelease;

        if (!force && !isDevBuild && !IsNewer(latestVersion, currentVersion))
        {
            console.MarkupLine($"[green]tinyprobe {Markup.Escape(currentVersion)} is up to date.[/]");
            return 0;
        }

        if (checkOnly)
        {
            console.MarkupLine($"New version available: [bold]{Markup.Escape(latestVersion)}[/] (current {Markup.Escape(currentVersion)}). Run [bold]tinyprobe update[/] to install it.");
            return 0;
        }

        if (isDevBuild && !force)
        {
            console.MarkupLine($"[yellow]This is a development build ({Markup.Escape(currentVersion)}).[/] Use [bold]tinyprobe update --force[/] to replace it with {Markup.Escape(latestVersion)}.");
            return 1;
        }

        var target = Environment.ProcessPath;
        if (target is null || !Path.GetFileNameWithoutExtension(target).Equals("tinyprobe", StringComparison.OrdinalIgnoreCase))
        {
            console.MarkupLine("[red]tinyprobe update only works on an installed tinyprobe binary.[/]");
            return 1;
        }

        try
        {
            byte[] binary = null!;
            await console.Status().StartAsync($"Downloading tinyprobe {Markup.Escape(latestVersion)}", async _ =>
            {
                var baseUrl = $"https://github.com/{Repository}/releases/download/{latestTag}";
                var archive = await http.GetByteArrayAsync($"{baseUrl}/{assetName}");
                var checksums = await http.GetStringAsync($"{baseUrl}/SHA256SUMS");

                var expected = FindChecksum(checksums, assetName)
                    ?? throw new InvalidOperationException($"{assetName} is missing from SHA256SUMS.");
                var actual = Convert.ToHexStringLower(SHA256.HashData(archive));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Checksum mismatch for {assetName}.");

                binary = ExtractBinary(archive, assetName);
            });

            ReplaceExecutable(target, binary);
        }
        catch (UnauthorizedAccessException)
        {
            console.MarkupLine($"[red]Cannot write to {Markup.Escape(Path.GetDirectoryName(target)!)}.[/] Re-run with the permissions needed for that directory, or reinstall with the install script.");
            return 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException or InvalidDataException)
        {
            console.MarkupLine($"[red]Update failed:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        var arrow = console.Profile.Capabilities.Unicode ? "→" : "->";
        console.MarkupLine($"[green]Updated tinyprobe {Markup.Escape(currentVersion)} {arrow} {Markup.Escape(latestVersion)}[/] ({Markup.Escape(target)})");
        return 0;
    }

    /// <summary>On Windows the replaced binary is left as <c>tinyprobe.exe.old</c>; remove it on the next run.</summary>
    public static void CleanupPreviousVersion()
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } path)
            return;

        try
        {
            File.Delete(path + ".old");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still in use or not ours to delete: try again next time.
        }
    }

    private static async Task<string> GetLatestTagAsync(HttpClient http)
    {
        var json = await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest");
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("tag_name").GetString()
            ?? throw new InvalidOperationException("The latest release has no tag.");
    }

    // A Native AOT binary reports the RID it was built for: an Alpine build updates to the Alpine build.
    internal static string? CurrentOs() =>
        OperatingSystem.IsWindows() ? "win"
        : OperatingSystem.IsLinux() && RuntimeInformation.RuntimeIdentifier.StartsWith("linux-musl", StringComparison.Ordinal) ? "linux-musl"
        : OperatingSystem.IsLinux() ? "linux"
        : OperatingSystem.IsMacOS() ? "osx"
        : null;

    /// <summary>Archive name of the release asset for a platform, or null when there is none.</summary>
    internal static string? GetAssetName(string? os, Architecture architecture)
    {
        var arch = architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null
        };
        if (os is null || arch is null)
            return null;

        var rid = $"{os}-{arch}";
        if (!SupportedRids.Contains(rid))
            return null;

        return os == "win" ? $"tinyprobe-{rid}.zip" : $"tinyprobe-{rid}.tar.gz";
    }

    /// <summary>Parses <c>major.minor.patch</c> with an optional <c>-prerelease</c> suffix.</summary>
    internal static bool TryParseVersion(string value, out Version version, out bool isPrerelease)
    {
        var core = value.Trim().TrimStart('v').Split('+')[0];
        var dash = core.IndexOf('-');
        isPrerelease = dash >= 0;
        if (isPrerelease)
            core = core[..dash];

        if (Version.TryParse(core, out var parsed) && parsed.Build >= 0 && parsed.Revision < 0)
        {
            version = parsed;
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }

    /// <summary>True when <paramref name="candidate"/> is a newer release than <paramref name="current"/>.</summary>
    internal static bool IsNewer(string candidate, string current)
    {
        if (!TryParseVersion(candidate, out var next, out var nextIsPrerelease) ||
            !TryParseVersion(current, out var now, out var nowIsPrerelease))
            return false;

        var comparison = next.CompareTo(now);
        // Same numbers: a final release is newer than its pre-release (1.0.0 > 1.0.0-beta).
        return comparison > 0 || (comparison == 0 && nowIsPrerelease && !nextIsPrerelease);
    }

    /// <summary>Finds the hash of <paramref name="fileName"/> in a <c>sha256sum</c> output.</summary>
    internal static string? FindChecksum(string checksums, string fileName)
    {
        foreach (var line in checksums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*') == fileName)
                return parts[0];
        }

        return null;
    }

    private static byte[] ExtractBinary(byte[] archive, string assetName)
    {
        using var input = new MemoryStream(archive);
        using var output = new MemoryStream();

        if (assetName.EndsWith(".zip", StringComparison.Ordinal))
        {
            using var zip = new ZipArchive(input, ZipArchiveMode.Read);
            var entry = zip.GetEntry("tinyprobe.exe")
                ?? throw new InvalidDataException("tinyprobe.exe not found in the archive.");
            using var stream = entry.Open();
            stream.CopyTo(output);
        }
        else
        {
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            TarEntry? entry;
            while ((entry = tar.GetNextEntry()) is not null)
            {
                if (Path.GetFileName(entry.Name) == "tinyprobe" && entry.DataStream is not null)
                {
                    entry.DataStream.CopyTo(output);
                    break;
                }
            }

            if (output.Length == 0)
                throw new InvalidDataException("tinyprobe not found in the archive.");
        }

        return output.ToArray();
    }

    private static void ReplaceExecutable(string target, byte[] binary)
    {
        var directory = Path.GetDirectoryName(target)!;
        // Staged in the same directory so the final move is a rename on the same file system.
        var staged = Path.Combine(directory, $".{Path.GetFileName(target)}.new");
        try
        {
            File.WriteAllBytes(staged, binary);
            MoveIntoPlace(staged, target);
        }
        finally
        {
            File.Delete(staged); // no-op once moved into place
        }
    }

    private static void MoveIntoPlace(string staged, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(staged,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            // rename(2) is atomic and the running process keeps its own copy of the old file.
            File.Move(staged, target, overwrite: true);
            return;
        }

        // A running .exe cannot be overwritten on Windows, but it can be renamed.
        var old = target + ".old";
        File.Delete(old);
        File.Move(target, old);
        try
        {
            File.Move(staged, target);
        }
        catch
        {
            File.Move(old, target);
            throw;
        }
    }
}
