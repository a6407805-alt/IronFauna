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
        private const string CacheVersion = "v2";   // bump when the SAPI styling changes so old renders are redone
        private const float MaxQueueAge = 2.6f;
        private const float StalePlayRequest = 1.8f;

        private AudioSource _src;      // SAPI fallback: deep stadium-PA chain
        private AudioSource _clean;    // recorded lines (Resources/Announcer): already mixed, played as they are
        private readonly HashSet<string> _recordedKeys = new HashSet<string>();
        private bool _workerTried;
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

            var cleanGo = new GameObject("AnnouncerVoice");
            cleanGo.transform.SetParent(transform, false);
            _clean = cleanGo.AddComponent<AudioSource>();
            _clean.playOnAwake = false;
            _clean.spatialBlend = 0f;
            _clean.priority = 0;
            // the PowerShell speech worker only starts if a line has no recorded clip
        }

        private bool Busy => _src.isPlaying || _clean.isPlaying;

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
                    RedirectStandardError = true,
                };
                _worker = Process.Start(psi);
                _worker.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) _rendered.Enqueue(e.Data); };
                _worker.BeginOutputReadLine();
                _worker.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) _rendered.Enqueue("STDERR|" + e.Data); };
                _worker.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[IronFauna Announcer] Voice worker unavailable ({ex.Message}); banner only.");
                _worker = null;
            }
#endif
        }

        /// <summary>
        /// PowerShell + System.Speech: male adult voice; for each "key|ssml" line render key.tmp.wav (the .wav extension
        /// matters: the engine writes a proper WAV header), rename it to key.wav and print "key|path", or "ERR|key|message".
        /// </summary>
        private const string WorkerScript = @"
$ErrorActionPreference = 'Stop'
$dir = $args[0]
Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
try { $s.SelectVoiceByHints([System.Speech.Synthesis.VoiceGender]::Male, [System.Speech.Synthesis.VoiceAge]::Adult) } catch { }
$s.Volume = 100
[Console]::Out.WriteLine('READY|' + $s.Voice.Name)
[Console]::Out.Flush()
while (($line = [Console]::In.ReadLine()) -ne $null) {
  $i = $line.IndexOf('|')
  if ($i -lt 1) { continue }
  $key = $line.Substring(0, $i)
  $ssml = $line.Substring($i + 1)
  $path = Join-Path $dir ($key + '.wav')
  $tmp = Join-Path $dir ($key + '.tmp.wav')
  try {
    $s.SetOutputToWaveFile($tmp)
    $s.SpeakSsml($ssml)
    $s.SetOutputToNull()
    Move-Item -Force $tmp $path
    [Console]::Out.WriteLine($key + '|' + $path)
  } catch {
    try { $s.SetOutputToNull() } catch { }
    [Console]::Out.WriteLine('ERR|' + $key + '|' + $_.Exception.Message)
  }
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
                if (priority >= 5) { _src.Stop(); _clean.Stop(); }
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
            if (recorded != null) { _clips[key] = recorded; _recordedKeys.Add(key); return true; }

            if (!_workerTried) { _workerTried = true; StartWorker(); }
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
                _worker.StandardInput.WriteLine(key + "|" + ToSsml(text));
                _worker.StandardInput.Flush();
            }
            catch { }
            return false;
        }

        /// <summary>Announcer delivery (W3C SSML): low, slow, loud, stressed, with a beat between words ("TRIPLE ... STRIKE!").</summary>
        private static string ToSsml(string text)
        {
            string safe = text.Replace("&", "and").Replace("<", " ").Replace(">", " ").Replace("|", " ").Replace("\"", " ");
            string[] words = safe.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            sb.Append("<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"en-US\">");
            sb.Append("<prosody pitch=\"low\" rate=\"slow\" volume=\"x-loud\">");
            bool shortCall = words.Length <= 3;
            for (int i = 0; i < words.Length; i++)
            {
                if (i > 0) sb.Append(shortCall ? "<break time=\"140ms\"/>" : " ");
                if (shortCall || i == words.Length - 1) sb.Append("<emphasis level=\"strong\">").Append(words[i]).Append("</emphasis>");
                else sb.Append(words[i]);
            }
            sb.Append("</prosody></speak>");
            return sb.ToString();
        }

        private IEnumerator LoadClip(string key, string path)
        {
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.WAV))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    UnityEngine.Debug.LogWarning($"[IronFauna Announcer] Could not load '{key}' from {path}: {req.error}");
                    yield break;
                }
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null || clip.length <= 0.05f)
                {
                    UnityEngine.Debug.LogWarning($"[IronFauna Announcer] '{key}' loaded empty from {path}");
                    yield break;
                }
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
                string head = line.Substring(0, i), rest = line.Substring(i + 1);
                if (head == "READY") { UnityEngine.Debug.Log($"[IronFauna Announcer] Voice ready: {rest}"); continue; }
                if (head == "ERR" || head == "STDERR") { UnityEngine.Debug.LogWarning($"[IronFauna Announcer] Voice render problem: {rest}"); continue; }
                StartCoroutine(LoadClip(head, rest));
            }

            if (Busy || _queue.Count == 0) return;
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
                var src = _recordedKeys.Contains(p.key) ? _clean : _src;
                src.volume = IronFauna.Core.GameSettings.AnnouncerVolume;
                src.clip = clip;
                src.Play();
            }
        }
    }
}
