using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace IronFauna.EditorTools
{
    /// <summary>
    /// IronFauna > Publish Update: one button that builds the Windows game, zips it and uploads it to the "latest"
    /// GitHub release, where the IronFauna Launcher picks it up (release-info.json, patch-notes.json, the zip and the
    /// optional launcher art). Friends just open the launcher: it sees the new version and updates by itself.
    ///
    /// Needs a GitHub fine-grained token (Repository: IronFauna only; Permissions: Contents = Read and write), saved
    /// on this PC only (EditorPrefs), never in the project.
    /// Order of uploads: zip, art, patch notes, then release-info.json LAST, so a launcher never sees a version whose
    /// zip isn't on the server yet.
    /// </summary>
    public class IronFaunaPublisher : EditorWindow
    {
        private const string Repo = "a6407805-alt/IronFauna";
        private const string TokenKey = "IronFauna.PublishToken";
        private const string BuildDir = "Builds/Windows";
        private const string ExeName = "IronFauna.exe";
        private const string ZipName = "IronFauna-Windows.zip";
        public const string ArtPath = "Builds/launcher-art.png";
        private const string PublicDownload = "https://github.com/" + Repo + "/releases/latest/download/";

        private string _token = "";
        private string _version = "";
        private string _title = "";
        private string _notes = "";
        private bool _includeArt = true;
        private Vector2 _scroll;

        // background upload state (written from a worker thread, read by OnGUI)
        private volatile bool _running;
        private volatile string _status = "";
        private volatile float _progress;
        private volatile bool _failed;
        private CancellationTokenSource _cts;

        [MenuItem("IronFauna/Publish Update...", priority = 0)]
        private static void Open()
        {
            var w = GetWindow<IronFaunaPublisher>(true, "Publish IronFauna Update");
            w.minSize = new Vector2(520, 560);
        }

        [MenuItem("IronFauna/Capture Launcher Art (Play mode)", priority = 1)]
        private static void CaptureArt()
        {
            Directory.CreateDirectory("Builds");
            ScreenCapture.CaptureScreenshot(ArtPath, 2);
            Debug.Log("[IronFauna] Launcher art saved to " + ArtPath + " (it goes up with the next Publish Update).");
        }

        [MenuItem("IronFauna/Capture Launcher Art (Play mode)", true)]
        private static bool CaptureArtValid() => Application.isPlaying;

        private void OnEnable()
        {
            _token = EditorPrefs.GetString(TokenKey, "");
            _version = NextVersion(PlayerSettings.bundleVersion);
            EditorApplication.update += Tick;
        }

        private void OnDisable() => EditorApplication.update -= Tick;

        private void Tick()
        {
            if (_running) Repaint();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Publish a new build to your friends' launchers", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Builds Windows, zips it and uploads it to the 'latest' release on github.com/" + Repo +
                                    ". The IronFauna Launcher installs it the next time a friend opens it.\n" +
                                    "Friends download the launcher once: github.com/" + Repo + "/releases/latest", MessageType.Info);

            using (new EditorGUI.DisabledScope(_running))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("GitHub token (saved on this PC only)", EditorStyles.miniBoldLabel);
                EditorGUILayout.BeginHorizontal();
                string t = EditorGUILayout.PasswordField(_token);
                if (t != _token) { _token = t.Trim(); EditorPrefs.SetString(TokenKey, _token); }
                if (GUILayout.Button("Get a token", GUILayout.Width(100))) Application.OpenURL("https://github.com/settings/personal-access-tokens/new");
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("Fine-grained token → Repository access: only IronFauna → Permissions: Contents = Read and write.", EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.Space();
                _version = EditorGUILayout.TextField("Version", _version);
                EditorGUILayout.LabelField("Current: " + PlayerSettings.bundleVersion, EditorStyles.miniLabel);
                _title = EditorGUILayout.TextField("Headline", _title);
                EditorGUILayout.LabelField("Patch notes (one change per line)");
                _notes = EditorGUILayout.TextArea(_notes, GUILayout.MinHeight(140));

                EditorGUILayout.Space();
                bool hasArt = File.Exists(ArtPath);
                _includeArt = EditorGUILayout.ToggleLeft(hasArt ? "Send launcher art (" + ArtPath + ")" : "No launcher art yet: enter Play mode, then IronFauna > Capture Launcher Art", _includeArt && hasArt);

                EditorGUILayout.Space();
                bool ready = !string.IsNullOrEmpty(_token) && !string.IsNullOrWhiteSpace(_version);
                using (new EditorGUI.DisabledScope(!ready || EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("BUILD & PUBLISH " + _version, GUILayout.Height(40))) Publish();
                }
                if (EditorApplication.isPlayingOrWillChangePlaymode) EditorGUILayout.HelpBox("Stop Play mode to publish.", MessageType.Warning);
            }

            if (_running || !string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.Space();
                Rect r = GUILayoutUtility.GetRect(18, 20, "TextField");
                EditorGUI.ProgressBar(r, _progress, _status);
                if (_running && GUILayout.Button("Cancel upload")) _cts?.Cancel();
                if (_failed) EditorGUILayout.HelpBox(_status, MessageType.Error);
            }
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ build (main thread), then upload (worker)
        private void Publish()
        {
            _failed = false;
            _status = "Building Windows player...";
            _progress = 0.02f;
            string version = _version.Trim();

            // 1. build
            if (Directory.Exists(BuildDir)) Directory.Delete(BuildDir, true);
            Directory.CreateDirectory(BuildDir);
            string previousVersion = PlayerSettings.bundleVersion;
            PlayerSettings.bundleVersion = version;
            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes) if (s.enabled) scenes.Add(s.path);
            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = Path.Combine(BuildDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                PlayerSettings.bundleVersion = previousVersion;
                Fail("Build failed (" + report.summary.result + "). See the Console.");
                return;
            }
            foreach (string d in Directory.GetDirectories(BuildDir, "*DoNotShip")) Directory.Delete(d, true);   // debug symbols

            // 2. zip + json files
            _status = "Zipping...";
            _progress = 0.1f;
            string zipPath = Path.Combine("Builds", ZipName);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(BuildDir, zipPath, System.IO.Compression.CompressionLevel.Optimal, false);
            long zipSize = new FileInfo(zipPath).Length;
            bool art = _includeArt && File.Exists(ArtPath);
            string published = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            string title = string.IsNullOrWhiteSpace(_title) ? "IronFauna " + version : _title.Trim();
            string notes = Bulleted(_notes);
            string info = "{" +
                          "\"version\":" + Q(version) + ",\"zip\":" + Q(ZipName) + ",\"zipSize\":" + zipSize +
                          ",\"exe\":" + Q(ExeName) + ",\"published\":" + Q(published) + ",\"title\":" + Q(title) +
                          ",\"notes\":" + Q(notes) + ",\"art\":" + Q(art ? Path.GetFileName(ArtPath) : "") + "}";
            string token = _token;
            AssetDatabase.SaveAssets();

            // 3. upload on a worker thread; the window keeps showing progress
            _cts = new CancellationTokenSource();
            _running = true;
            CancellationToken ct = _cts.Token;
            Task.Run(async () =>
            {
                try
                {
                    await Upload(token, version, published, title, notes, zipPath, art, info, ct);
                    _status = "Published " + version + "! Friends get it next time they open the launcher.";
                    _progress = 1f;
                }
                catch (Exception ex)
                {
                    _failed = true;
                    _status = ex is OperationCanceledException ? "Upload cancelled." : "Upload failed: " + ex.Message;
                }
                finally
                {
                    _running = false;
                }
            });
            _version = NextVersion(version);
            _title = "";
            _notes = "";
        }

        private void Fail(string message)
        {
            _failed = true;
            _status = message;
            _running = false;
            Debug.LogError("[IronFauna] " + message);
        }

        // ------------------------------------------------------------------ GitHub
        [Serializable] private class GhAsset { public long id; public string name; }
        [Serializable] private class GhRelease { public long id; public GhAsset[] assets; }
        [Serializable] private class NoteList { public List<Note> items = new List<Note>(); }
        [Serializable] private class Note { public string version; public string date; public string title; public string notes; }

        private async Task Upload(string token, string version, string published, string title, string notes, string zipPath, bool art, string info, CancellationToken ct)
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("IronFauna-Publisher/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            string api = "https://api.github.com/repos/" + Repo;

            // the release (created on first publish)
            _status = "Finding the release...";
            GhRelease rel;
            using (var res = await http.GetAsync(api + "/releases/tags/latest", ct))
            {
                if (res.StatusCode == HttpStatusCode.NotFound)
                {
                    string body = "{\"tag_name\":\"latest\",\"name\":\"IronFauna (latest)\",\"make_latest\":\"true\",\"body\":" + Q(ReleaseBody(version, title, notes)) + "}";
                    using var created = await http.PostAsync(api + "/releases", new StringContent(body, Encoding.UTF8, "application/json"), ct);
                    await Ensure(created, "create the release");
                    rel = JsonUtility.FromJson<GhRelease>(await created.Content.ReadAsStringAsync());
                }
                else
                {
                    await Ensure(res, "read the release");
                    rel = JsonUtility.FromJson<GhRelease>(await res.Content.ReadAsStringAsync());
                }
            }

            // patch notes history (newest first, last 30)
            var history = new NoteList();
            try
            {
                using var anon = new HttpClient();
                anon.DefaultRequestHeaders.UserAgent.ParseAdd("IronFauna-Publisher/1.0");
                string old = await anon.GetStringAsync(PublicDownload + "patch-notes.json");
                history = JsonUtility.FromJson<NoteList>("{\"items\":" + old + "}") ?? new NoteList();
            }
            catch (Exception) { /* first publish: no history yet */ }
            history.items.RemoveAll(n => n.version == version);
            history.items.Insert(0, new Note { version = version, date = published, title = title, notes = notes });
            if (history.items.Count > 30) history.items.RemoveRange(30, history.items.Count - 30);
            string notesJson = JsonUtility.ToJson(history);
            notesJson = notesJson.Substring("{\"items\":".Length, notesJson.Length - "{\"items\":".Length - 1);

            await Put(http, rel, ZipName, File.ReadAllBytes(zipPath), "application/zip", 0.12f, 0.92f, ct);
            if (art) await Put(http, rel, Path.GetFileName(ArtPath), File.ReadAllBytes(ArtPath), "image/png", 0.92f, 0.95f, ct);
            await Put(http, rel, "patch-notes.json", Encoding.UTF8.GetBytes(notesJson), "application/json", 0.95f, 0.97f, ct);
            await Put(http, rel, "release-info.json", Encoding.UTF8.GetBytes(info), "application/json", 0.97f, 0.99f, ct);

            string patch = "{\"body\":" + Q(ReleaseBody(version, title, notes)) + ",\"make_latest\":\"true\"}";
            using var p = await http.SendAsync(new HttpRequestMessage(new HttpMethod("PATCH"), api + "/releases/" + rel.id)
            { Content = new StringContent(patch, Encoding.UTF8, "application/json") }, ct);
            await Ensure(p, "update the release notes");
        }

        /// <summary>Replaces one release asset (delete the old one with that name, upload the new one).</summary>
        private async Task Put(HttpClient http, GhRelease rel, string name, byte[] data, string type, float from, float to, CancellationToken ct)
        {
            if (rel.assets != null)
            {
                foreach (var a in rel.assets)
                {
                    if (a.name != name) continue;
                    using var del = await http.DeleteAsync("https://api.github.com/repos/" + Repo + "/releases/assets/" + a.id, ct);
                    await Ensure(del, "replace " + name);
                }
            }
            _status = "Uploading " + name + " (" + (data.Length / 1048576f).ToString("0.0") + " MB)...";
            var content = new ProgressContent(data, type, f => _progress = Mathf.Lerp(from, to, f));
            string url = "https://uploads.github.com/repos/" + Repo + "/releases/" + rel.id + "/assets?name=" + Uri.EscapeDataString(name);
            using var res = await http.PostAsync(url, content, ct);
            await Ensure(res, "upload " + name);
        }

        private static async Task Ensure(HttpResponseMessage res, string what)
        {
            if (res.IsSuccessStatusCode) return;
            string body = await res.Content.ReadAsStringAsync();
            string hint = res.StatusCode == HttpStatusCode.Unauthorized ? " (the token is wrong or expired)"
                        : res.StatusCode == HttpStatusCode.Forbidden || res.StatusCode == HttpStatusCode.NotFound ? " (the token needs Contents: Read and write on IronFauna)"
                        : "";
            throw new Exception("couldn't " + what + ": " + (int)res.StatusCode + hint + " " + (body.Length > 200 ? body.Substring(0, 200) : body));
        }

        /// <summary>Streams a byte array and reports how much has gone up.</summary>
        private sealed class ProgressContent : HttpContent
        {
            private readonly byte[] _data;
            private readonly Action<float> _progress;

            public ProgressContent(byte[] data, string type, Action<float> progress)
            {
                _data = data;
                _progress = progress;
                Headers.ContentType = new MediaTypeHeaderValue(type);
            }

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                const int chunk = 1 << 18;
                for (int i = 0; i < _data.Length; i += chunk)
                {
                    int n = Math.Min(chunk, _data.Length - i);
                    await stream.WriteAsync(_data, i, n);
                    _progress((i + n) / (float)Math.Max(1, _data.Length));
                }
            }

            protected override bool TryComputeLength(out long length)
            {
                length = _data.Length;
                return true;
            }
        }

        // ------------------------------------------------------------------ helpers
        private static string ReleaseBody(string version, string title, string notes) =>
            "## " + title + "\n\n" + notes + "\n\n---\n**New here?** Download **IronFauna-Launcher.exe** below and run it. " +
            "It installs IronFauna and keeps it up to date (version " + version + ").";

        private static string Bulleted(string text)
        {
            var sb = new StringBuilder();
            foreach (string raw in (text ?? "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (!line.StartsWith("•") && !line.StartsWith("-")) line = "• " + line;
                else if (line.StartsWith("-")) line = "• " + line.Substring(1).TrimStart();
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(line);
            }
            return sb.ToString();
        }

        /// <summary>"0.3.4" → "0.3.5"; anything else becomes a three-part version first.</summary>
        public static string NextVersion(string current)
        {
            string[] parts = (current ?? "").Split('.');
            int[] v = new int[3];
            for (int i = 0; i < 3 && i < parts.Length; i++) int.TryParse(parts[i], out v[i]);
            if (v[0] == 0 && v[1] == 0 && v[2] == 0) return "0.1.0";
            v[2]++;
            return v[0] + "." + v[1] + "." + v[2];
        }

        private static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
