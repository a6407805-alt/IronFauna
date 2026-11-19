// ============================================================================================================
//  IronFauna Launcher
//
//  Friends download IronFauna-Launcher.exe once. Every time it opens it:
//    1. replaces itself if the launcher on the "latest" GitHub release changed (SelfUpdate.cs)
//    2. reads release-info.json from the release (version, zip name and size, patch notes)
//    3. compares it with <install folder>\version.txt and offers INSTALL / UPDATE / PLAY
//    4. downloads IronFauna-Windows.zip with progress, unpacks it beside the old copy and swaps the folders, so a
//       cancelled or failed update never leaves a broken game behind
//  The owner publishes a new game build from Unity (IronFauna > Publish Update); this launcher only reads.
//  Everything goes through the public "releases/latest/download/<file>" links: no GitHub API, no rate limits,
//  no login.
// ============================================================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IronFaunaLauncher
{
    public partial class MainWindow : Window
    {
        public const string Repo = "a6407805-alt/IronFauna";
        public const string LauncherAssetName = "IronFauna-Launcher.exe";
        private const string Download = "https://github.com/" + Repo + "/releases/latest/download/";

        public static readonly string[] Args = Environment.GetCommandLineArgs();
        private static readonly HttpClient Http = CreateHttp();

        private enum State { Checking, Install, Update, Play, Busy, Retry }

        private readonly LauncherSettings _settings = LauncherSettings.Load();
        private State _state = State.Checking;
        private bool _busy;
        private ReleaseInfo? _release;
        private CancellationTokenSource? _cts;

        /// <summary>--screenshot &lt;png&gt;: show demo data, save a picture of the window and exit (the CI preview).</summary>
        private static bool PreviewMode => Array.Exists(Args, a => a == "--screenshot");

        public MainWindow()
        {
            InitializeComponent();
            Version? v = Assembly.GetExecutingAssembly().GetName().Version;
            LauncherVersionText.Text = "LAUNCHER " + (v == null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}");
        }

        private static HttpClient CreateHttp()
        {
            var h = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("IronFauna-Launcher/1.0");
            return h;
        }

        // ------------------------------------------------------------------ startup
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            InstallDirText.Text = _settings.InstallDir;
            CloseOnPlayCheck.IsChecked = _settings.CloseOnPlay;
            AutoUpdateCheck.IsChecked = _settings.AutoUpdate;
            LoadCachedArt();
            RefreshInstalledText();

            if (PreviewMode)
            {
                ShowDemo();
                return;
            }
            if (await TrySelfUpdateAsync()) return;
            await CheckAsync();
        }

        private async void Window_ContentRendered(object? sender, EventArgs e)
        {
            if (!PreviewMode) return;
            int i = Array.IndexOf(Args, "--screenshot");
            string path = i >= 0 && i + 1 < Args.Length ? Args[i + 1] : "launcher-preview.png";
            await Task.Delay(800);
            try
            {
                var bmp = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(this);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = File.Create(path);
                enc.Save(fs);
            }
            finally
            {
                Application.Current.Shutdown();
            }
        }

        private void ShowDemo()
        {
            var demo = new List<PatchNote>
            {
                new PatchNote { Version = "v0.3.0", Date = "2026-11-18T20:00:00Z", Title = "Technical combat and seven new-look stages",
                    Notes = "• Stamina, blocks, parries and ripostes\n• Dodge with invincibility frames\n• Every stage rebuilt: skies, lighting, backdrops\n• Explosive props removed" },
                new PatchNote { Version = "v0.2.0", Date = "2026-11-17T20:00:00Z", Title = "Grapples, throws and limb tearing", Notes = "• Grab, throw, slam and tear limbs\n• 20 martial-arts specials" },
            };
            ShowNotes(demo);
            SetState(State.Update);
            SetStatus("Update available: v0.3.0", "412.6 MB download");
            SetProgress(0);
        }

        /// <summary>Reads the release and decides INSTALL / UPDATE / PLAY.</summary>
        private async Task CheckAsync()
        {
            SetState(State.Checking);
            SetStatus("Checking for updates...", "");
            SetProgress(0, indeterminate: true);
            try
            {
                _release = await GetReleaseAsync();
            }
            catch (Exception ex) when (IsNetworkError(ex) || ex is System.Text.Json.JsonException)
            {
                _release = null;
            }
            _ = LoadNotesAsync();

            string? installed = InstalledVersion();
            if (_release == null)
            {
                SetProgress(0);
                if (installed != null && File.Exists(ExePath(null)))
                {
                    SetState(State.Play);
                    SetStatus("Offline: playing " + installed, "Couldn't reach the update server. You can still play the version you have.");
                }
                else
                {
                    SetState(State.Retry);
                    SetStatus("Couldn't reach the update server", "Check your internet connection, or ask the host if a build has been published yet.");
                }
                return;
            }

            LatestVersionText.Text = Pretty(_release.Version);
            LatestDateText.Text = DatePretty(_release.Published);
            LatestTitleText.Text = string.IsNullOrWhiteSpace(_release.Title) ? "IronFauna " + Pretty(_release.Version) : _release.Title;
            LatestNotesText.Text = _release.Notes;
            _ = LoadArtAsync(_release);

            SetProgress(0);
            if (installed == null || !File.Exists(ExePath(_release)))
            {
                SetState(State.Install);
                SetStatus("Ready to install " + Pretty(_release.Version), SizeText(_release.ZipSize) + " download");
            }
            else if (!string.Equals(installed, _release.Version, StringComparison.Ordinal))
            {
                SetState(State.Update);
                SetStatus($"Update available: {Pretty(installed)} → {Pretty(_release.Version)}", SizeText(_release.ZipSize) + " download");
                if (_settings.AutoUpdate) await InstallAsync();
            }
            else
            {
                SetState(State.Play);
                SetStatus("Up to date: " + Pretty(installed), "Ready to fight.");
            }
        }

        private static async Task<ReleaseInfo> GetReleaseAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            string text = await Http.GetStringAsync(Download + "release-info.json", cts.Token);
            return Json.Parse<ReleaseInfo>(text) ?? throw new System.Text.Json.JsonException("empty release-info.json");
        }

        private async Task LoadNotesAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                string text = await Http.GetStringAsync(Download + "patch-notes.json", cts.Token);
                ShowNotes(Json.ParseNotes(text));
            }
            catch (Exception ex) when (IsNetworkError(ex) || ex is System.Text.Json.JsonException)
            {
                if (_release != null)
                    ShowNotes(new List<PatchNote> { new PatchNote { Version = Pretty(_release.Version), Date = _release.Published, Title = _release.Title, Notes = _release.Notes } });
            }
        }

        private void ShowNotes(List<PatchNote> notes)
        {
            NotesList.ItemsSource = notes;
            NotesEmptyText.Visibility = notes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (notes.Count > 0 && (_release == null || PreviewMode))
            {
                LatestVersionText.Text = notes[0].Version;
                LatestDateText.Text = notes[0].DateText;
                LatestTitleText.Text = notes[0].Title;
                LatestNotesText.Text = notes[0].Notes;
            }
        }

        // ------------------------------------------------------------------ hero art (a shot of the game, uploaded by the publisher)
        private void LoadCachedArt()
        {
            try
            {
                if (!File.Exists(LauncherSettings.ArtCachePath)) return;
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;   // don't keep the file locked
                img.UriSource = new Uri(LauncherSettings.ArtCachePath);
                img.EndInit();
                BgImage.Source = img;
            }
            catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is UriFormatException) { }
        }

        private async Task LoadArtAsync(ReleaseInfo release)
        {
            if (string.IsNullOrEmpty(release.Art) || _settings.ArtVersion == release.Version) return;
            try
            {
                byte[] data = await Http.GetByteArrayAsync(Download + release.Art);
                Directory.CreateDirectory(LauncherSettings.DataDir);
                await File.WriteAllBytesAsync(LauncherSettings.ArtCachePath, data);
                _settings.ArtVersion = release.Version;
                _settings.Save();
                LoadCachedArt();
            }
            catch (Exception ex) when (IsNetworkError(ex) || ex is IOException || ex is UnauthorizedAccessException) { }
        }

        // ------------------------------------------------------------------ install / update
        private async Task InstallAsync()
        {
            if (_release == null || _busy) return;
            _busy = true;
            SetState(State.Busy);
            _cts = new CancellationTokenSource();
            string dir = _settings.InstallDir;
            string parent = Path.GetDirectoryName(Path.GetFullPath(dir)) ?? LauncherSettings.DataDir;
            string zipPath = Path.Combine(LauncherSettings.DataDir, "download.zip");
            string newDir = dir.TrimEnd('\\', '/') + ".new";
            string oldDir = dir.TrimEnd('\\', '/') + ".old";
            try
            {
                Directory.CreateDirectory(LauncherSettings.DataDir);
                Directory.CreateDirectory(parent);

                // ---- download
                SetStatus("Downloading " + Pretty(_release.Version) + "...", "");
                var sw = Stopwatch.StartNew();
                using (HttpResponseMessage response = await Http.GetAsync(Download + _release.Zip, HttpCompletionOption.ResponseHeadersRead, _cts.Token))
                {
                    response.EnsureSuccessStatusCode();
                    long total = response.Content.Headers.ContentLength ?? _release.ZipSize;
                    await using Stream source = await response.Content.ReadAsStreamAsync(_cts.Token);
                    await using var target = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                    byte[] buffer = new byte[1 << 16];
                    long done = 0, lastUi = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), _cts.Token)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), _cts.Token);
                        done += read;
                        if (sw.ElapsedMilliseconds - lastUi > 150 || done == total)
                        {
                            lastUi = sw.ElapsedMilliseconds;
                            double secs = Math.Max(0.1, sw.Elapsed.TotalSeconds);
                            double rate = done / secs;
                            string eta = rate > 1 && total > 0 ? TimeSpan.FromSeconds((total - done) / rate).ToString(@"m\:ss") + " left" : "";
                            SetProgress(total > 0 ? done * 100.0 / total : 0);
                            DetailText.Text = $"{SizeText(done)} of {SizeText(total)}  ·  {SizeText((long)rate)}/s  ·  {eta}";
                        }
                    }
                }
                if (_release.ZipSize > 0 && new FileInfo(zipPath).Length != _release.ZipSize)
                    throw new IOException("The download was cut short. Try again.");

                // ---- unpack beside the current copy, then swap folders
                SetStatus("Installing...", "Unpacking files");
                SetProgress(0);
                TryDeleteDir(newDir);
                await Task.Run(() => Extract(zipPath, newDir, p => Dispatcher.Invoke(() => SetProgress(p))), _cts.Token);
                File.WriteAllText(Path.Combine(newDir, "version.txt"), _release.Version);

                TryDeleteDir(oldDir);
                if (Directory.Exists(dir)) Directory.Move(dir, oldDir);
                Directory.Move(newDir, dir);
                TryDeleteDir(oldDir);
                TryDelete(zipPath);

                RefreshInstalledText();
                SetProgress(100);
                SetState(State.Play);
                SetStatus("Up to date: " + Pretty(_release.Version), "Installed. Ready to fight.");
            }
            catch (OperationCanceledException)
            {
                SetState(State.Update);
                SetStatus("Update cancelled", "");
                SetProgress(0);
            }
            catch (Exception ex) when (IsNetworkError(ex) || ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                // the old copy is still in place (or put back): the game keeps working
                if (!Directory.Exists(dir) && Directory.Exists(oldDir)) { try { Directory.Move(oldDir, dir); } catch (IOException) { } }
                SetProgress(0);
                bool canPlay = File.Exists(ExePath(_release)) || File.Exists(ExePath(null));
                SetState(canPlay ? State.Play : State.Retry);
                SetStatus("Update failed", Friendly(ex) + (canPlay ? " You can still play the version you have." : ""));
            }
            finally
            {
                _busy = false;
                _cts = null;
            }
        }

        private static void Extract(string zipPath, string destination, Action<double> progress)
        {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            // a zip with one top-level folder (e.g. "IronFauna/...") is flattened so the exe lands in the install folder
            string? common = CommonRoot(zip.Entries.Select(en => en.FullName));
            int n = 0, count = Math.Max(1, zip.Entries.Count);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                if (common != null) name = name.Substring(common.Length);
                if (name.Length == 0) continue;
                string target = Path.GetFullPath(Path.Combine(destination, name));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;   // no "../" escapes
                if (name.EndsWith("/"))
                {
                    Directory.CreateDirectory(target);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
                if (++n % 25 == 0) progress(n * 100.0 / count);
            }
            progress(100);
        }

        private static string? CommonRoot(IEnumerable<string> names)
        {
            string? root = null;
            foreach (string raw in names)
            {
                string n = raw.Replace('\\', '/');
                int slash = n.IndexOf('/');
                if (slash < 0) return null;
                string first = n.Substring(0, slash + 1);
                if (root == null) root = first;
                else if (root != first) return null;
            }
            // only flatten a wrapper folder that is not the Unity data folder itself
            return root != null && !root.EndsWith("_Data/", StringComparison.OrdinalIgnoreCase) ? root : null;
        }

        // ------------------------------------------------------------------ play
        private void Launch()
        {
            string exe = ExePath(_release);
            if (!File.Exists(exe)) exe = ExePath(null);
            if (!File.Exists(exe))
            {
                SetState(State.Install);
                SetStatus("The game isn't installed", "Press INSTALL to download it.");
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" });
                SetStatus("Starting IronFauna...", "");
                if (_settings.CloseOnPlay) Application.Current.Shutdown();
                else WindowState = WindowState.Minimized;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                SetStatus("Couldn't start the game", ex.Message);
            }
        }

        /// <summary>The game exe: the release's exe name, or any *.exe in the install folder that has a *_Data folder beside it.</summary>
        private string ExePath(ReleaseInfo? release)
        {
            string dir = _settings.InstallDir;
            if (release != null && !string.IsNullOrEmpty(release.Exe)) return Path.Combine(dir, release.Exe);
            try
            {
                if (Directory.Exists(dir))
                {
                    foreach (string exe in Directory.GetFiles(dir, "*.exe"))
                    {
                        string name = Path.GetFileNameWithoutExtension(exe);
                        if (Directory.Exists(Path.Combine(dir, name + "_Data"))) return exe;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            return Path.Combine(dir, "IronFauna.exe");
        }

        private string? InstalledVersion()
        {
            try
            {
                string f = Path.Combine(_settings.InstallDir, "version.txt");
                return File.Exists(f) ? File.ReadAllText(f).Trim() : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return null; }
        }

        private void RefreshInstalledText()
        {
            string? v = InstalledVersion();
            InstalledVersionText.Text = v == null ? "Not installed yet." : "Installed: " + Pretty(v);
        }

        // ------------------------------------------------------------------ UI state
        private void SetState(State s)
        {
            _state = s;
            switch (s)
            {
                case State.Checking: PlayButton.Content = "PLAY"; PlayButton.IsEnabled = false; break;
                case State.Install: PlayButton.Content = "INSTALL"; PlayButton.IsEnabled = true; break;
                case State.Update: PlayButton.Content = "UPDATE"; PlayButton.IsEnabled = true; break;
                case State.Play: PlayButton.Content = "PLAY"; PlayButton.IsEnabled = true; break;
                case State.Busy: PlayButton.Content = "CANCEL"; PlayButton.IsEnabled = true; break;
                case State.Retry: PlayButton.Content = "RETRY"; PlayButton.IsEnabled = true; break;
            }
            bool idle = s != State.Busy;
            RepairButton.IsEnabled = idle && _release != null;
            UninstallButton.IsEnabled = idle;
        }

        private void SetStatus(string status, string detail)
        {
            StatusText.Text = status;
            DetailText.Text = detail;
        }

        private void SetProgress(double percent, bool indeterminate = false)
        {
            Progress.IsIndeterminate = indeterminate;
            Progress.Value = Math.Clamp(percent, 0, 100);
            PercentText.Text = indeterminate || percent <= 0 ? "" : $"{percent:0}%";
        }

        // ------------------------------------------------------------------ buttons
        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            switch (_state)
            {
                case State.Install:
                case State.Update: await InstallAsync(); break;
                case State.Play: Launch(); break;
                case State.Busy: _cts?.Cancel(); break;
                case State.Retry: await CheckAsync(); break;
            }
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (HomePage == null || NotesPage == null || SettingsPage == null) return;   // fires during InitializeComponent
            HomePage.Visibility = TabHome.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            NotesPage.Visibility = TabNotes.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            SettingsPage.Visibility = TabSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ReadNotes_Click(object sender, RoutedEventArgs e) => TabNotes.IsChecked = true;

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Close();
        }

        private void SettingsCheck_Click(object sender, RoutedEventArgs e)
        {
            _settings.CloseOnPlay = CloseOnPlayCheck.IsChecked == true;
            _settings.AutoUpdate = AutoUpdateCheck.IsChecked == true;
            _settings.Save();
        }

        private async void ChangeFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose where IronFauna is installed" };
            if (dlg.ShowDialog(this) != true) return;
            string chosen = dlg.FolderName;
            // pick the folder itself if it already holds the game, else install into an IronFauna subfolder
            bool hasGame = File.Exists(Path.Combine(chosen, "version.txt"));
            _settings.InstallDir = hasGame ? chosen : Path.Combine(chosen, "IronFauna");
            _settings.Save();
            InstallDirText.Text = _settings.InstallDir;
            RefreshInstalledText();
            await CheckAsync();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string dir = Directory.Exists(_settings.InstallDir) ? _settings.InstallDir : LauncherSettings.DataDir;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
        }

        private async void Repair_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || _release == null) return;
            TabHome.IsChecked = true;
            await InstallAsync();
        }

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || !Directory.Exists(_settings.InstallDir)) return;
            if (MessageBox.Show(this, "Delete the installed game from\n" + _settings.InstallDir + "?\n\nYour settings and saved mechs are kept.",
                    "Uninstall IronFauna", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            TryDeleteDir(_settings.InstallDir);
            RefreshInstalledText();
            _ = CheckAsync();
        }

        private void Shortcut_Click(object sender, RoutedEventArgs e)
        {
            string? me = Environment.ProcessPath;
            if (me == null) return;
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic link = shell.CreateShortcut(Path.Combine(desktop, "IronFauna.lnk"));
                link.TargetPath = me;
                link.WorkingDirectory = Path.GetDirectoryName(me);
                link.IconLocation = me + ",0";
                link.Description = "IronFauna Launcher";
                link.Save();
                SetStatus(StatusText.Text, "Desktop shortcut created.");
            }
            catch (Exception ex)
            {
                SetStatus(StatusText.Text, "Couldn't create the shortcut: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ helpers
        private static string Pretty(string version) => string.IsNullOrEmpty(version) ? "" : version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : "v" + version;

        private static string DatePretty(string iso) => new PatchNote { Date = iso }.DateText;

        private static string SizeText(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            double mb = bytes / (1024.0 * 1024.0);
            return mb >= 1024 ? $"{mb / 1024.0:0.00} GB" : $"{mb:0.0} MB";
        }

        public static bool IsNetworkError(Exception ex) =>
            ex is HttpRequestException || ex is TaskCanceledException || ex is IOException || ex is System.Net.Sockets.SocketException;

        private static string Friendly(Exception ex) => ex switch
        {
            HttpRequestException h when h.StatusCode == System.Net.HttpStatusCode.NotFound => "The build isn't on the server yet.",
            HttpRequestException => "Lost the connection to the update server.",
            UnauthorizedAccessException => "Windows blocked writing to the install folder. Close the game if it's running, or pick another folder in Settings.",
            InvalidDataException => "The download was damaged. Press UPDATE to try again.",
            _ => ex.Message,
        };

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        private static void TryDeleteDir(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }
    }
}
