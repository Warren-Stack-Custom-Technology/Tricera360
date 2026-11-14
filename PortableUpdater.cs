using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Tricera360;

internal static class PortableUpdater
{
    private const string Repo = "Warren-Stack-Custom-Technology/Tricera360";
    private const string ReadyEventPrefix = @"Local\Tricera360.UpdateReady.";
    private const string StagingPrefix = ".tricera360-update-";
    private const string BackupName = "Previous-Tricera360.exe";
    private const string HelperName = "Tricera360.Update.exe";
    private const int MaxManifest = 4096;

    internal static Version InstalledVersion { get; } = new(typeof(PortableUpdater).Assembly.GetName().Version!.ToString(3));
    internal static bool Available =>
#if TRICERA_PORTABLE
        string.Equals(Path.GetFileName(Environment.ProcessPath), UpdatePackage.ExeName, StringComparison.OrdinalIgnoreCase) &&
        ReadPublicKey() is not null;
#else
        false;
#endif

    private static string PreferencePath => Path.Combine(SettingsStore.DefaultDirectory, "updates.json");

    internal static bool? ReadPreference()
    {
        try
        {
            if (!File.Exists(PreferencePath)) return null;
            return JsonSerializer.Deserialize<bool>(File.ReadAllText(PreferencePath));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            DiagnosticLog.Session.Warning("UPDATE_PREFERENCE_READ_FAILED", error: error);
            return false;
        }
    }

    internal static bool SavePreference(bool enabled)
    {
        try
        {
            SettingsStore.WriteJson(PreferencePath, enabled, JsonSerializerOptions.Default);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Session.Warning("UPDATE_PREFERENCE_SAVE_FAILED", error: error);
            return false;
        }
    }

    private static string? ReadPublicKey()
    {
        using Stream? stream = typeof(PortableUpdater).Assembly.GetManifestResourceStream("Tricera360.UpdatePublicKey");
        if (stream is null) return null;
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static RSA LoadKey()
    {
        string key = ReadPublicKey() ?? throw new InvalidDataException("The update signing key is missing.");
        RSA rsa = RSA.Create();
        try
        {
            rsa.FromXmlString(key);
            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    internal static async Task<bool> StageAndLaunchAsync(Func<bool> canRestart, CancellationToken cancellation)
    {
        string? staging = null;
        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Tricera360/" + InstalledVersion);
            using (CancellationTokenSource lookup = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                lookup.CancelAfter(TimeSpan.FromSeconds(10));
                using var metadata = new MemoryStream();
                await DownloadAsync(client, $"https://api.github.com/repos/{Repo}/releases/latest",
                    metadata, 256 * 1024, lookup.Token);
                metadata.Position = 0;
                Release? release = await JsonSerializer.DeserializeAsync<Release>(metadata, cancellationToken: lookup.Token);
                if (release is not { Draft: false, Prerelease: false, Tag: not null } ||
                    !release.Tag.StartsWith('v') || !UpdatePackage.TryVersion(release.Tag[1..], out Version version) ||
                    version <= InstalledVersion) return false;

                string zipName = $"Tricera360-{release.Tag}-win-x64.zip";
                string[] files = [UpdatePackage.ManifestName, UpdatePackage.SignatureName, zipName];
                if (release.Assets is null || files.Any(name => !release.Assets.Any(asset => asset.Name == name)))
                    return false;
                if (ReadPreference() != true || !canRestart()) return false;

                staging = Path.Combine(SettingsStore.InstallDirectory, StagingPrefix + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                WriteVersion(staging, version);
                string releaseUrl = $"https://github.com/{Repo}/releases/download/{release.Tag}";
                UpdateManifest manifest = await DownloadFilesAsync(client, releaseUrl, staging, files, version, cancellation);
                string stagedFolder = staging;
                await Task.Run(() => UpdatePackage.ExtractExecutableAsync(Path.Combine(stagedFolder, zipName),
                    Path.Combine(stagedFolder, UpdatePackage.ExeName), manifest, cancellation), cancellation);
            }

            cancellation.ThrowIfCancellationRequested();
            if (ReadPreference() != true || !canRestart()) return false;
            string helperDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tricera360", "Updater");
            Directory.CreateDirectory(helperDirectory);
            string helper = Path.Combine(helperDirectory, HelperName);
            File.Copy(Environment.ProcessPath!, helper, overwrite: true);
            using Process process = Start(helper, "--apply-update", staging, Environment.ProcessId.ToString());
            staging = null;
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return false; }
        catch (Exception error) when (IsUpdateError(error))
        {
            DiagnosticLog.Session.Warning("UPDATE_CHECK_FAILED", error: error);
            return false;
        }
        finally
        {
            if (staging is not null) CleanStaging(staging);
        }
    }

    private static async Task<UpdateManifest> DownloadFilesAsync(HttpClient client, string releaseUrl, string staging,
        string[] files, Version version, CancellationToken cancellation)
    {
        using CancellationTokenSource download = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        download.CancelAfter(TimeSpan.FromMinutes(3));
        foreach (string file in files.Take(2))
        {
            await using FileStream target = new(Path.Combine(staging, file), FileMode.CreateNew);
            await DownloadAsync(client, $"{releaseUrl}/{file}", target, MaxManifest, download.Token);
        }

        // Authenticate the release before requesting the much larger ZIP.
        UpdateManifest manifest = ReadManifest(staging, version);
        await using FileStream archive = new(Path.Combine(staging, files[2]), FileMode.CreateNew);
        await DownloadAsync(client, $"{releaseUrl}/{files[2]}", archive, UpdatePackage.MaxDownload, download.Token);
        return manifest;
    }

    private static async Task DownloadAsync(HttpClient client, string url, Stream target, int limit,
        CancellationToken cancellation)
    {
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit)
            throw new InvalidDataException("The update exceeded its size limit.");
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellation);
        await UpdatePackage.CopyLimitedAsync(source, target, limit, cancellation);
    }

    private static void WriteVersion(string staging, Version version) =>
        File.WriteAllText(Path.Combine(staging, "version.txt"), version.ToString());

    private static Version ReadVersion(string staging)
    {
        if (!UpdatePackage.TryVersion(File.ReadAllText(Path.Combine(staging, "version.txt")), out Version version) ||
            version <= InstalledVersion)
            throw new InvalidDataException("The staged update version is invalid.");
        return version;
    }

    private static UpdateManifest ReadManifest(string staging, Version version)
    {
        using RSA key = LoadKey();
        return UpdatePackage.VerifyManifest(File.ReadAllBytes(Path.Combine(staging, UpdatePackage.ManifestName)),
            File.ReadAllBytes(Path.Combine(staging, UpdatePackage.SignatureName)), key, version);
    }

    internal static int Apply(string staging, int parentId)
    {
        if (!string.Equals(Path.GetFileName(Environment.ProcessPath), HelperName, StringComparison.OrdinalIgnoreCase) ||
            ReadPublicKey() is null) return 1;

        staging = Path.GetFullPath(staging);
        string folderName = Path.GetFileName(staging);
        if (!folderName.StartsWith(StagingPrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(folderName[StagingPrefix.Length..], "N", out _))
            return 1;

        string installFolder = Path.GetDirectoryName(staging)!;
        string target = Path.Combine(installFolder, UpdatePackage.ExeName);
        string backup = Path.Combine(staging, BackupName);
        try
        {
            if (!WaitForOriginal(parentId, target))
                throw new IOException("Tricera360 did not close for the update.");
            FileVersionInfo current = FileVersionInfo.GetVersionInfo(target);
            if (new Version(current.FileMajorPart, current.FileMinorPart, current.FileBuildPart) != InstalledVersion)
                throw new IOException("The installed executable changed while preparing the update.");
            string staged = Path.Combine(staging, UpdatePackage.ExeName);
            UpdatePackage.VerifyExecutable(staged, ReadManifest(staging, ReadVersion(staging)));

            File.Replace(staged, target, backup);
            string readyId = Guid.NewGuid().ToString("N");
            using EventWaitHandle ready = new(false, EventResetMode.ManualReset, ReadyEventPrefix + readyId);
            using (Process updated = Start(target, "--updated", readyId))
            {
                long deadline = Environment.TickCount64 + 30000;
                while (Environment.TickCount64 < deadline)
                {
                    if (ready.WaitOne(250))
                    {
                        CleanStaging(staging);
                        return 0;
                    }
                    if (updated.HasExited) break;
                }
                if (!updated.HasExited)
                {
                    updated.Kill();
                    if (!updated.WaitForExit(5000)) throw new IOException("The new version did not close.");
                }
            }
            throw new IOException("The new version did not finish opening.");
        }
        catch (Exception error) when (IsUpdateError(error))
        {
            DiagnosticLog.Session.Warning("UPDATE_INSTALL_FAILED", error: error);
            try
            {
                if (File.Exists(backup))
                {
                    if (File.Exists(target)) File.Replace(backup, target, null);
                    else File.Move(backup, target);
                }
                if (WaitForOriginal(parentId, target))
                {
                    using Process recovered = Start(target, "--skip-update");
                }
                CleanStaging(staging);
                return 1;
            }
            catch (Exception recoveryError) when (IsUpdateError(recoveryError))
            {
                MessageBox.Show($"The update could not be recovered automatically.\n\n{recoveryError.Message}\n\n" +
                    $"Update files: {staging}", "Tricera360", MessageBoxButton.OK, MessageBoxImage.Warning);
                return 1;
            }
        }
    }

    internal static void SignalReady(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return;
        if (!EventWaitHandle.TryOpenExisting(ReadyEventPrefix + id, out EventWaitHandle? ready)) return;
        using (ready) ready?.Set();
    }

    private static bool WaitForOriginal(int parentId, string target)
    {
        try
        {
            using Process parent = Process.GetProcessById(parentId);
            if (!string.Equals(parent.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase))
                return true;
            return parent.WaitForExit(15000);
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
    }

    private static Process Start(string file, params string[] arguments)
    {
        ProcessStartInfo start = new(file) { WorkingDirectory = Path.GetDirectoryName(file)! };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start Tricera360.");
    }

    private static bool IsUpdateError(Exception error) => error is IOException or UnauthorizedAccessException
        or HttpRequestException or JsonException or CryptographicException or Win32Exception
        or InvalidDataException or InvalidOperationException or OperationCanceledException or ArgumentException;

    private static void CleanStaging(string staging)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(staging))
            {
                string name = Path.GetFileName(file);
                if (name is UpdatePackage.ExeName or UpdatePackage.ManifestName or UpdatePackage.SignatureName
                    or BackupName or "version.txt" ||
                    name.StartsWith("Tricera360-v", StringComparison.Ordinal) &&
                    name.EndsWith("-win-x64.zip", StringComparison.Ordinal)) File.Delete(file);
            }
            Directory.Delete(staging);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Session.Warning("UPDATE_CLEANUP_FAILED", error: error);
        }
    }

    private sealed record Release([property: JsonPropertyName("tag_name")] string? Tag,
        [property: JsonPropertyName("draft")] bool Draft, [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] Asset[]? Assets);
    private sealed record Asset([property: JsonPropertyName("name")] string Name);
}
