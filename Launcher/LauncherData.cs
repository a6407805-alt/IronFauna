using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronFaunaLauncher
{
    /// <summary>release-info.json on the "latest" release, written by Unity's IronFauna > Publish Update.</summary>
    public sealed class ReleaseInfo
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("zip")] public string Zip { get; set; } = "IronFauna-Windows.zip";
        [JsonPropertyName("zipSize")] public long ZipSize { get; set; }
        [JsonPropertyName("exe")] public string Exe { get; set; } = "IronFauna.exe";
        [JsonPropertyName("published")] public string Published { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("notes")] public string Notes { get; set; } = "";
        [JsonPropertyName("art")] public string Art { get; set; } = "";
    }

    /// <summary>launcher-info.json on the "latest" release, written by the GitHub workflow that builds this exe.</summary>
    public sealed class LauncherInfo
    {
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("commit")] public string Commit { get; set; } = "";
    }

    /// <summary>One entry of patch-notes.json (newest first).</summary>
    public sealed class PatchNote
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("date")] public string Date { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("notes")] public string Notes { get; set; } = "";

        [JsonIgnore]
        public string DateText => DateTime.TryParse(Date, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime d)
            ? d.ToLocalTime().ToString("MMMM d, yyyy", CultureInfo.CurrentCulture).ToUpperInvariant()
            : Date;
    }

    /// <summary>The launcher's own settings: %LOCALAPPDATA%\IronFauna\launcher.json.</summary>
    public sealed class LauncherSettings
    {
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IronFauna");
        private static readonly string FilePath = Path.Combine(DataDir, "launcher.json");

        [JsonPropertyName("installDir")] public string InstallDir { get; set; } = Path.Combine(DataDir, "Game");
        [JsonPropertyName("closeOnPlay")] public bool CloseOnPlay { get; set; } = true;
        [JsonPropertyName("autoUpdate")] public bool AutoUpdate { get; set; } = true;
        [JsonPropertyName("artVersion")] public string ArtVersion { get; set; } = "";

        public static string ArtCachePath => Path.Combine(DataDir, "launcher-art.jpg");

        public static LauncherSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(FilePath));
                    if (s != null && !string.IsNullOrWhiteSpace(s.InstallDir)) return s;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException) { }
            return new LauncherSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }
    }

    public static class Json
    {
        public static T? Parse<T>(string text) where T : class => JsonSerializer.Deserialize<T>(text);
        public static List<PatchNote> ParseNotes(string text) => JsonSerializer.Deserialize<List<PatchNote>>(text) ?? new List<PatchNote>();
    }
}
