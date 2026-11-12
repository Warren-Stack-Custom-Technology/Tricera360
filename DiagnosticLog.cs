using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Tricera360;

internal sealed class DiagnosticLog
{
    private const int RetainedFiles = 12;
    private static readonly Regex LogName = new(
        @"^Tricera360-\d{8}-\d{9}-\d+-\d+\.log$", RegexOptions.CultureInvariant);
    private readonly object _gate = new();
    private readonly string _root;
    private readonly string _fallback;
    private readonly long _maxBytes;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _sessionId = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Environment.ProcessId}";
    private readonly Dictionary<string, Incident> _incidents = new();
    private string? _file;
    private long _bytes;
    private int _part;

    public static DiagnosticLog Session { get; } = new(FindProjectRoot(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tricera360", "Logs"));

    public DiagnosticLog(string root, string fallback, long maxBytes = 1024 * 1024,
        Func<DateTimeOffset>? clock = null)
    {
        (_root, _fallback, _maxBytes, _clock) = (root, fallback, maxBytes, clock ?? (() => DateTimeOffset.UtcNow));
    }

    public string? CurrentFile { get { lock (_gate) return _file; } }

    public void Info(string code, string detail = "") => Write("INFO", code, detail, null, null);

    public void Warning(string code, string detail = "", Exception? error = null, string? key = null) =>
        Write("WARN", code, detail, error, key);

    public void Error(string code, string detail = "", Exception? error = null, string? key = null) =>
        Write("ERROR", code, detail, error, key);

    private void Write(string level, string code, string detail, Exception? error, string? key)
    {
        lock (_gate)
        {
            DateTimeOffset now = _clock();
            if (key is not null)
            {
                string incidentKey = $"{code}:{key}";
                if (_incidents.TryGetValue(incidentKey, out Incident? incident))
                {
                    if (now - incident.First < TimeSpan.FromSeconds(30))
                    {
                        incident.Skipped++;
                        return;
                    }
                    if (incident.Skipped != 0) detail += $" repeated={incident.Skipped}";
                }

                if (_incidents.Count >= 128)
                    _incidents.Remove(_incidents.MinBy(item => item.Value.First).Key);
                _incidents[incidentKey] = new Incident(now);
            }

            string failure = error is null ? "" : $" exception={error.GetType().Name} hr=0x{error.HResult:X8} message={error.Message}";
            string frame = error?.StackTrace?.Split('\n', 2)[0].Trim() ?? "";
            string context = frame.Length == 0 ? "" : $" at={frame}";
            string message = $"{now.ToString("O", CultureInfo.InvariantCulture)} {level,-5} {code} {OneLine(detail + failure + context)}".TrimEnd();
            byte[] line = Encoding.UTF8.GetBytes(message + Environment.NewLine);
            string folder = _file is null ? _root : Path.GetDirectoryName(_file)!;

            if (TryAppend(folder, line)) return;
            if (!string.Equals(folder, _fallback, StringComparison.OrdinalIgnoreCase))
            {
                _file = null;
                _bytes = 0;
                byte[] fallbackLine = Encoding.UTF8.GetBytes(
                    $"{now:O} WARN LOG_FALLBACK Project folder unavailable.{Environment.NewLine}{message}{Environment.NewLine}");
                if (TryAppend(_fallback, fallbackLine)) return;
            }
            _file = null;
            Trace.WriteLine(message);
        }
    }

    private bool TryAppend(string folder, byte[] line)
    {
        try
        {
            bool newFile = _file is null || (_bytes > 0 && _bytes + line.Length > _maxBytes);
            if (newFile) Directory.CreateDirectory(folder);
            string path = newFile ? Path.Combine(folder, $"Tricera360-{_sessionId}-{++_part:00}.log") : _file!;
            using (var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                file.Write(line);
            _file = path;
            _bytes = (newFile ? 0 : _bytes) + line.Length;
            if (newFile) Prune(folder);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Trace.WriteLine($"Tricera360 log write failed ({error.GetType().Name}).");
            return false;
        }
    }

    private void Prune(string folder)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder, "Tricera360-*.log")
                .Where(file => file != _file && LogName.IsMatch(Path.GetFileName(file)))
                .OrderByDescending(File.GetLastWriteTimeUtc).Skip(RetainedFiles - 1))
                File.Delete(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Tricera360 log retention skipped ({error.GetType().Name}).");
        }
    }

    private static string OneLine(string value)
    {
        value = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        return value.Length <= 640 ? value : value[..637] + "...";
    }

    private static string FindProjectRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        if (File.Exists(Path.Combine(folder.FullName, "Tricera360.csproj"))) return folder.FullName;
        DirectoryInfo? project = folder;
        while (project is not null && project.Name != "bin") project = project.Parent;
        if (project?.Parent is DirectoryInfo root && File.Exists(Path.Combine(root.FullName, "Tricera360.csproj")))
            return root.FullName;
        return folder.FullName;
    }

    private sealed class Incident(DateTimeOffset first)
    {
        public DateTimeOffset First { get; } = first;
        public int Skipped { get; set; }
    }
}
