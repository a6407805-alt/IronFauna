using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace IronFauna.Brawl
{
    /// <summary>
    /// Arena announcer voice (owner 2026-11-22: "fun, Halo-style callouts, not a flat mono TTS voice").
    ///
    /// For every callout key it plays, in order of preference:
    ///  1. a recorded clip at <c>Assets/Resources/Announcer/&lt;key&gt;.wav</c> (drop your own voice lines in, same key names);
    ///  2. otherwise a Windows SAPI line rendered to a WAV file by a background PowerShell worker (male voice,
    ///     slower, lower pitch, short pauses between words), cached on disk so it is rendered only once.
    ///
    /// Either way the clip plays through a deep "stadium PA" chain: pitched down, arena reverb, slap-back echo and a
    /// touch of grit. Callouts queue up and play back-to-back like multi-kill medals; stale ones are dropped.
    /// </summary>
    public class AnnouncerVoice : MonoBehaviour
    {
        private const string CacheVersion = "v1";   // bump when the SAPI styling changes so old renders are redone
        private const float MaxQueueAge = 2.6f;
        private const float StalePlayRequest = 1.8f;

        private AudioSource _src;
        private Process _worker;
        private string _dir;
        private readonly ConcurrentQueue<string> _rendered = new ConcurrentQueue<string>();
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, string> _texts = new Dictionary<string, string>();
        private readonly HashSet<string> _requested = new HashSet<string>();
        private readonly Dictionary<string, float> _wantPlay = new Dictionary<string, float>();

        private struct Pending { public string key; public int priority; public float t; }
        private readonly List<Pending> _queue = new List<Pending>();

        // ------------------------------------------------------------------ setup
        private void Awake()
        {
            var go = new GameObject("AnnouncerPA");
            go.transform.SetParent(transform, false);
            _src = go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
            _src.volume = 1f;
            _src.pitch = 0.86f;                          // deeper, slower, larger-than-life

            var hp = go.AddComponent<AudioHighPassFilter>();
            hp.cutoffFrequency = 110f;
            var dist = go.AddComponent<AudioDistortionFilter>();
            dist.distortionLevel = 0.16f;                // PA-horn grit
            var echo = go.AddComponent<AudioEchoFilter>();
            echo.delay = 120f;
            echo.decayRatio = 0.28f;
            echo.wetMix = 0.24f;
            echo.dryMix = 1f;
            var rev = go.AddComponent<AudioReverbFilter>();
            rev.reverbPreset = AudioReverbPreset.User;
            rev.dryLevel = 0f;
            rev.room = -700f;
            rev.roomHF = -500f;
            rev.decayTime = 2.1f;
            rev.decayHFRatio = 0.6f;
            rev.reflectionsLevel = -500f;
            rev.reverbLevel = -250f;
            rev.reverbDelay = 0.035f;
            rev.diffusion = 100f;
            rev.density = 100f;

            StartWorker();
        }

        private void OnDestroy()
        {
            if (_worker == null) return;
            try
            {
                if (!_worker.HasExited)
                {
                    _worker.StandardInput.Close();
                    _worker.Kill();
                }
                _worker.Dispose();
            }
            catch { }
            _worker = null;
        }

        private void StartWorker()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                _dir = Path.Combine(Application.temporaryCachePath, "AnnouncerVoice_" + CacheVersion);
                Directory.CreateDirectory(_dir);
                string script = Path.Combine(_dir, "render.ps1");
                File.WriteAllText(script, WorkerScript, new UTF8Encoding(false));

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" \"{_dir}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                };
                _worker = Process.Start(psi);
                _worker.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) _rendered.Enqueue(e.Data); };
                _worker.BeginOutputReadLine();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[IronFauna Announcer] Voice worker unavailable ({ex.Message}); banner only.");
                _worker = null;
            }
#endif
        }

        /// <summary>PowerShell: pick a male SAPI voice, then for each "key|sapi-xml" line render key.wav and print "key|path".</summary>
        private const string WorkerScript = @"
$ErrorActionPreference = 'SilentlyContinue'
$dir = $args[0]
$v = New-Object -ComObject SAPI.SpVoice
foreach ($tok in $v.GetVoices()) { if ($tok.GetAttribute('Gender') -eq 'Male') { $v.Voice = $tok; break } }
$v.Rate = -1
$v.Volume = 100
while (($line = [Console]::In.ReadLine()) -ne $null) {
  $i = $line.IndexOf('|')
  if ($i -lt 1) { continue }
  $key = $line.Substring(0, $i)
  $xml = $line.Substring($i + 1)
  $path = Join-Path $dir ($key + '.wav')
  $tmp = $path + '.part'
  $fs = New-Object -ComObject SAPI.SpFileStream
  $fs.Format.Type = 22
  $fs.Open($tmp, 3, $false)
  $v.AudioOutputStream = $fs
  [void]$v.Speak($xml, 8)
  $fs.Close()
  Move-Item -Force $tmp $path
  [Console]::Out.WriteLine($key + '|' + $path)
  [Console]::Out.Flush()
}
";

        // ------------------------------------------------------------------ public API
        /// <summary>Registers a callout and renders it in the background so it plays instantly later.</summary>
        public void Prewarm(string key, string text)
        {
            _texts[key] = text;
            EnsureClip(key);
        }

        /// <summary>Queues a callout. Higher priority (4+) cuts the queue, like a match-ending call.</summary>
        public void Say(string key, string text, int priority)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!string.IsNullOrEmpty(text)) _texts[key] = text;
            if (priority >= 4)
            {
                _queue.Clear();
                if (_src.isPlaying && priority >= 5) _src.Stop();
            }
            if (_clips.ContainsKey(key) || EnsureClip(key))
            {
                _queue.Add(new Pending { key = key, priority = priority, t = Time.unscaledTime });
                if (_queue.Count > 3)
                {
                    int lowest = 0;
                    for (int i = 1; i < _queue.Count; i++) if (_queue[i].priority < _queue[lowest].priority) lowest = i;
                    _queue.RemoveAt(lowest);
                }
            }
            else
            {
                _wantPlay[key] = Time.unscaledTime;   // play as soon as the render arrives, if still fresh
            }
        }

        // ------------------------------------------------------------------ internals
        /// <summary>True when the clip is ready now; otherwise starts loading or rendering it.</summary>
        private bool EnsureClip(string key)
        {
            if (_clips.ContainsKey(key)) return true;

            var recorded = Resources.Load<AudioClip>("Announcer/" + key);
            if (recorded != null) { _clips[key] = recorded; return true; }

            if (_requested.Contains(key) || _dir == null) return false;
            _requested.Add(key);

            string file = Path.Combine(_dir, key + ".wav");
            if (File.Exists(file) && new FileInfo(file).Length > 1000)
            {
                StartCoroutine(LoadClip(key, file));
                return false;
            }
            if (_worker == null || _worker.HasExited || !_texts.TryGetValue(key, out string text)) return false;
            try
            {
                _worker.StandardInput.WriteLine(key + "|" + ToSapiXml(text));
                _worker.StandardInput.Flush();
            }
            catch { }
            return false;
        }

        /// <summary>Announcer delivery: low pitch, slow, emphasised, with a beat between words ("TRIPLE ... STRIKE!").</summary>
        private static string ToSapiXml(string text)
        {
            string safe = text.Replace("&", "and").Replace("<", " ").Replace(">", " ").Replace("|", " ");
            string[] words = safe.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            sb.Append("<pitch absmiddle=\"-6\"><rate absspeed=\"-2\"><volume level=\"100\">");
            bool shortCall = words.Length <= 3;
            for (int i = 0; i < words.Length; i++)
            {
                if (i > 0) sb.Append(shortCall ? "<silence msec=\"110\"/>" : " ");
                if (shortCall || i == words.Length - 1) sb.Append("<emph>").Append(words[i]).Append("</emph>");
                else sb.Append(words[i]);
            }
            sb.Append("</volume></rate></pitch>");
            return sb.ToString();
        }

        private IEnumerator LoadClip(string key, string path)
        {
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.WAV))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) yield break;
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null) yield break;
                clip.name = "Announcer_" + key;
                _clips[key] = clip;
                if (_wantPlay.TryGetValue(key, out float asked))
                {
                    _wantPlay.Remove(key);
                    if (Time.unscaledTime - asked < StalePlayRequest)
                        _queue.Add(new Pending { key = key, priority = 2, t = Time.unscaledTime });
                }
            }
        }

        private void Update()
        {
            while (_rendered.TryDequeue(out string line))
            {
                int i = line.IndexOf('|');
                if (i < 1) continue;
                StartCoroutine(LoadClip(line.Substring(0, i), line.Substring(i + 1)));
            }

            if (_src.isPlaying || _queue.Count == 0) return;
            // next fresh callout (highest priority first, then oldest)
            int pick = -1;
            for (int k = _queue.Count - 1; k >= 0; k--)
            {
                if (Time.unscaledTime - _queue[k].t > MaxQueueAge) { _queue.RemoveAt(k); continue; }
            }
            for (int k = 0; k < _queue.Count; k++)
            {
                if (pick < 0 || _queue[k].priority > _queue[pick].priority) pick = k;
            }
            if (pick < 0) return;
            var p = _queue[pick];
            _queue.RemoveAt(pick);
            if (_clips.TryGetValue(p.key, out var clip) && clip != null)
            {
                _src.clip = clip;
                _src.Play();
            }
        }
    }
}
