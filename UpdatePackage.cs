using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tricera360;

internal sealed record UpdateManifest(string Version, string Sha256, long Size);

internal static class UpdatePackage
{
    internal const string ManifestName = "Tricera360-update.json";
    internal const string SignatureName = "Tricera360-update.sig";
    internal const string ExeName = "Tricera360.exe";
    internal const int MaxDownload = 160 * 1024 * 1024;

    internal static bool TryVersion(string? text, out Version version)
    {
        version = new Version();
        if (!Version.TryParse(text, out Version? parsed) || parsed.Build < 0 || parsed.Revision >= 0 ||
            parsed.ToString() != text) return false;
        version = parsed;
        return true;
    }

    internal static UpdateManifest VerifyManifest(byte[] json, byte[] signature, RSA key, Version expected)
    {
        if (!key.VerifyData(json, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new InvalidDataException("The update signature is invalid.");
        UpdateManifest? manifest = JsonSerializer.Deserialize<UpdateManifest>(json);
        if (manifest is null || !TryVersion(manifest.Version, out Version version) || version != expected ||
            manifest.Sha256 is not { Length: 64 } || !manifest.Sha256.All(Uri.IsHexDigit) ||
            manifest.Size is <= 0 or > MaxDownload)
            throw new InvalidDataException("The signed update does not match the expected release.");
        return manifest;
    }

    internal static void VerifyExecutable(string path, UpdateManifest manifest)
    {
        using FileStream stream = File.OpenRead(path);
        if (stream.Length != manifest.Size ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), Convert.FromHexString(manifest.Sha256)))
            throw new InvalidDataException("The executable does not match the signed update.");
    }

    internal static async Task CopyLimitedAsync(Stream source, Stream destination, long limit, CancellationToken cancellation)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
        {
            total += read;
            if (total > limit) throw new InvalidDataException("The update exceeded its size limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellation);
        }
    }

    internal static async Task ExtractExecutableAsync(string archivePath, string path, UpdateManifest manifest,
        CancellationToken cancellation)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        ZipArchiveEntry[] entries = archive.Entries.Where(e => e.FullName == ExeName).ToArray();
        if (entries.Length != 1 || entries[0].Length != manifest.Size)
            throw new InvalidDataException("The update ZIP does not contain the expected executable.");

        await using (FileStream destination = new(path, FileMode.CreateNew))
        await using (Stream source = entries[0].Open())
            await CopyLimitedAsync(source, destination, manifest.Size, cancellation);
        VerifyExecutable(path, manifest);
    }
}
