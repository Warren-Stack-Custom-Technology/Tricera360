using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tricera360;

internal static class UpdatePackageTests
{
    internal static async Task RunAsync()
    {
        using RSA signer = RSA.Create(2048);
        using RSA verifier = RSA.Create();
        verifier.FromXmlString(signer.ToXmlString(false));
        Version version = new(0, 6, 4);
        byte[] payload = [1, 3, 5, 7];
        UpdateManifest manifest = new(version.ToString(), Convert.ToHexString(SHA256.HashData(payload)), payload.Length);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(manifest);
        byte[] signature = signer.SignData(json, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        Check(UpdatePackage.VerifyManifest(json, signature, verifier, version) == manifest, "Valid signed manifest");
        Check(!UpdatePackage.TryVersion("0.6.4-rc1", out _) && !UpdatePackage.TryVersion("0.06.4", out _),
            "Reject non-stable and ambiguous versions");
        await RejectAsync(() => Task.FromResult(UpdatePackage.VerifyManifest(json, signature, verifier, new Version(0, 6, 5))));
        byte[] altered = (byte[])json.Clone();
        altered[^2] ^= 1;
        await RejectAsync(() => Task.FromResult(UpdatePackage.VerifyManifest(altered, signature, verifier, version)));
        using RSA otherSigner = RSA.Create(2048);
        await RejectAsync(() => Task.FromResult(UpdatePackage.VerifyManifest(json,
            otherSigner.SignData(json, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), verifier, version)));

        string folder = Directory.CreateTempSubdirectory("Tricera360-update-test-").FullName;
        try
        {
            string archivePath = Path.Combine(folder, "update.zip");
            string output = Path.Combine(folder, UpdatePackage.ExeName);
            WriteArchive(archivePath, payload, includeDuplicate: false);
            await UpdatePackage.ExtractExecutableAsync(archivePath, output, manifest, CancellationToken.None);
            Check(File.ReadAllBytes(output).AsSpan().SequenceEqual(payload), "Signed executable extracted");
            Check(!File.Exists(Path.Combine(folder, "Profiles", "updates.json")), "Archive profile entry ignored");

            File.Delete(output);
            WriteArchive(archivePath, [1, 3, 5, 8], includeDuplicate: false);
            await RejectAsync(() => UpdatePackage.ExtractExecutableAsync(archivePath, output, manifest, CancellationToken.None));

            File.Delete(output);
            WriteArchive(archivePath, payload, includeDuplicate: true);
            await RejectAsync(() => UpdatePackage.ExtractExecutableAsync(archivePath, output, manifest, CancellationToken.None));
            using var tooLarge = new MemoryStream(payload);
            using var destination = new MemoryStream();
            await RejectAsync(() => UpdatePackage.CopyLimitedAsync(tooLarge, destination, 3, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
        Console.WriteLine("Passed signed update manifest, tamper rejection, version checks, and bounded extraction.");
    }

    private static void WriteArchive(string path, byte[] payload, bool includeDuplicate)
    {
        using ZipArchive archive = new(File.Create(path), ZipArchiveMode.Create);
        using (Stream entry = archive.CreateEntry(UpdatePackage.ExeName).Open()) entry.Write(payload);
        if (includeDuplicate) archive.CreateEntry(UpdatePackage.ExeName);
        archive.CreateEntry("Profiles/updates.json");
    }

    private static async Task RejectAsync(Func<Task> action)
    {
        try { await action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("An invalid update was accepted.");
    }

    private static void Check(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }
}
