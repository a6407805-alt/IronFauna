// ============================================================================================================
//  IronFauna Launcher - self-update (same approach as the Broken Characters launcher)
//
//  Every start: read launcher-info.json from the latest release (the workflow that builds the launcher writes the
//  exe's size there). If it differs from this exe: download IronFauna-Launcher.exe next to this one as ".new",
//  check its size, rename the running exe to ".old" (Windows allows renaming a running program), move the new one
//  into place and restart with "--updated" (which skips the check, so it can never loop).
//  Anything going wrong just skips the update; the old launcher keeps working and tries again next time.
// ============================================================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace IronFaunaLauncher
{
    public partial class MainWindow
    {
        private static bool JustUpdated => Array.Exists(Args, a => a == "--updated");

        /// <summary>Returns true when the launcher is restarting into a newer copy of itself.</summary>
        private async Task<bool> TrySelfUpdateAsync()
        {
#if DEBUG
            await Task.CompletedTask;
            return false;   // never replace a developer's own build
#else
            if (PreviewMode || JustUpdated) return false;
            string? me = Environment.ProcessPath;
            string myName = me == null ? "" : Path.GetFileName(me);
            // only "IronFauna-Launcher*.exe" (also "IronFauna-Launcher (1).exe" from a second download) replaces itself
            if (me == null || !myName.StartsWith("IronFauna-Launcher", StringComparison.OrdinalIgnoreCase)
                || !myName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;

            string newPath = me + ".new", oldPath = me + ".old";
            TryDelete(oldPath);
            TryDelete(newPath);

            LauncherInfo? info;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                info = Json.Parse<LauncherInfo>(await Http.GetStringAsync(Download + "launcher-info.json", cts.Token));
            }
            catch (Exception ex) when (IsNetworkError(ex) || ex is System.Text.Json.JsonException)
            {
                return false;
            }
            long mine;
            try { mine = new FileInfo(me).Length; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return false; }
            if (info == null || info.Size <= 0 || info.Size == mine) return false;

            SetStatus("Updating the launcher...", "");
            SetProgress(0, indeterminate: true);
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
                using (HttpResponseMessage response = await Http.GetAsync(Download + LauncherAssetName, HttpCompletionOption.ResponseHeadersRead, cts.Token))
                {
                    response.EnsureSuccessStatusCode();
                    await using Stream source = await response.Content.ReadAsStreamAsync(cts.Token);
                    await using var target = new FileStream(newPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                    await source.CopyToAsync(target, cts.Token);
                }
                if (new FileInfo(newPath).Length != info.Size) { TryDelete(newPath); return false; }
                File.Move(me, oldPath);
                try { File.Move(newPath, me); }
                catch (Exception)
                {
                    if (!File.Exists(me) && File.Exists(oldPath)) File.Move(oldPath, me);
                    throw;
                }
            }
            catch (Exception ex) when (IsNetworkError(ex) || ex is UnauthorizedAccessException)
            {
                TryDelete(newPath);
                SetProgress(0);
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo(me) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(me) ?? "" };
                psi.ArgumentList.Add("--updated");
                Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                SetStatus("Launcher updated", "Close and reopen it to use the new version.");
                return false;
            }
            Application.Current.Shutdown();
            return true;
#endif
        }
    }
}
