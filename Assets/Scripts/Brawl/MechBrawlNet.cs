using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using IronFauna.Boxing;
using IronFauna.Pilots;

namespace IronFauna.Brawl
{
    public enum BrawlNetRole
    {
        Offline = 0,
        Host = 1,
        Client = 2,
    }

    /// <summary>
    /// Online play over Unity Relay (no port forwarding): the host opens a Relay room and gets a short join code
    /// (e.g. K7QX2M) that friends type in. Host-authoritative: the host simulates every robot, friends send their
    /// controller input, and the host streams the full ragdoll pose of every robot back ~20 times a second.
    /// This class only moves packets; MechBrawlMatch decides what goes in them.
    /// </summary>
    public class MechBrawlNet : MonoBehaviour
    {
        public const int MaxRemotePlayers = 7;
        private const uint Magic = 0x49464239; // "IFB9"

        public const byte PHello = 1;      // client -> host (reliable): pilot JSON
        public const byte PWelcome = 2;    // host -> client (reliable): your client id
        public const byte PIntent = 3;     // client -> host (unreliable): controller input
        public const byte PSetup = 4;      // host -> clients (reliable): arena seed + roster for a new round
        public const byte PSnapshot = 5;   // host -> clients (unreliable): every robot's pose and status
        public const byte PBanner = 6;     // host -> clients (reliable): centre-screen callout
        public const byte PHit = 7;        // host -> clients (unreliable): hit sparks / sounds
        public const byte PResult = 8;     // host -> one client (reliable): round pay for their pilot

        public BrawlNetRole role = BrawlNetRole.Offline;
        public string JoinCode { get; private set; } = "";
        public string Status { get; private set; } = "";
        /// <summary>Host: room open and code ready. Client: connected and welcomed.</summary>
        public bool Ready { get; private set; }
        public bool Failed { get; private set; }
        public int MyClientId { get; private set; } = -1;
        /// <summary>Client: the robot this PC controls (its input is sent to the host).</summary>
        public MechBoxer LocalGhost;

        public class RemotePlayer
        {
            public int id;
            public NetworkConnection conn;
            public PilotProfile pilot;
            public float lastSeen;
            // latest input from this friend
            public Vector2 move;
            public bool guard, sprint, grab, crouch, sit;
            public PunchType lastPunch;
            public int lastSlip;
            public int punchSerial, slipSerial;
            public int appliedPunch = -1, appliedSlip = -1;
            public float camYaw;          // their camera's heading: movement is camera-relative
            public Transform camProxy;     // host-side stand-in for that camera
        }

        public readonly List<RemotePlayer> Players = new List<RemotePlayer>();

        public event Action<RemotePlayer> PlayerJoined;
        public event Action<RemotePlayer> PlayerLeft;
        /// <summary>Client: a host packet arrived (type, body).</summary>
        public event Action<byte, byte[]> HostPacket;
        public event Action Disconnected;

        private NetworkDriver _driver;
        private NetworkPipeline _reliable, _unreliable;
        private NetworkConnection _toHost;
        private readonly List<NetworkConnection> _pending = new List<NetworkConnection>();
        private int _nextId = 1;
        private float _intentAccum;
        private bool _destroyed;

        // ------------------------------------------------------------------ start / stop
        public async void StartNetwork(BrawlNetRole netRole, string joinCode)
        {
            StopNetwork();
            role = netRole;
            if (role == BrawlNetRole.Offline) return;
            Failed = false;
            Ready = false;
            try
            {
                Status = "SIGNING IN TO UNITY SERVICES...";
                if (UnityServices.State != ServicesInitializationState.Initialized)
                    await UnityServices.InitializeAsync();
                if (_destroyed) return;
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                if (_destroyed) return;

                if (role == BrawlNetRole.Host)
                {
                    Status = "OPENING ONLINE ROOM...";
                    Allocation a = await RelayService.Instance.CreateAllocationAsync(MaxRemotePlayers);
                    if (_destroyed) return;
                    string code = await RelayService.Instance.GetJoinCodeAsync(a.AllocationId);
                    if (_destroyed) return;
                    var ep = PickEndpoint(a.ServerEndpoints);
                    var data = new Unity.Networking.Transport.Relay.RelayServerData(ep.Host, (ushort)ep.Port, a.AllocationIdBytes, a.ConnectionData, a.ConnectionData, a.Key, ep.Secure, false);
                    CreateDriver(ref data);
                    if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0) throw new Exception("could not bind to the Relay server");
                    if (_driver.Listen() != 0) throw new Exception("could not listen on the Relay server");
                    JoinCode = code.ToUpperInvariant();
                    Ready = true;
                    Status = $"JOIN CODE  {JoinCode}";
                    Debug.Log($"[IronFauna Net] Hosting online room. Join code: {JoinCode}");
                }
                else
                {
                    string code = (joinCode ?? "").Trim().ToUpperInvariant();
                    if (code.Length == 0) throw new Exception("no join code entered");
                    Status = $"JOINING {code}...";
                    JoinAllocation j = await RelayService.Instance.JoinAllocationAsync(code);
                    if (_destroyed) return;
                    var ep = PickEndpoint(j.ServerEndpoints);
                    var data = new Unity.Networking.Transport.Relay.RelayServerData(ep.Host, (ushort)ep.Port, j.AllocationIdBytes, j.ConnectionData, j.HostConnectionData, j.Key, ep.Secure, false);
                    CreateDriver(ref data);
                    if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0) throw new Exception("could not bind to the Relay server");
                    _toHost = _driver.Connect(data.Endpoint);
                    JoinCode = code;
                    Status = $"CONNECTING TO {code}...";
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        private void Fail(Exception ex)
        {
            Failed = true;
            Ready = false;
            string msg = ex.Message ?? "";
            Debug.LogWarning($"[IronFauna Net] Online failed: {ex}");
            if (msg.IndexOf("project", StringComparison.OrdinalIgnoreCase) >= 0 || msg.IndexOf("cloud", StringComparison.OrdinalIgnoreCase) >= 0)
                Status = "ONLINE FAILED: LINK THE PROJECT IN EDIT > PROJECT SETTINGS > SERVICES, THEN TURN ON RELAY";
            else if (role == BrawlNetRole.Client && (msg.IndexOf("join", StringComparison.OrdinalIgnoreCase) >= 0 || msg.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0))
                Status = "COULDN'T FIND THAT JOIN CODE. CHECK IT AND TRY AGAIN";
            else
                Status = "ONLINE FAILED: " + msg.ToUpperInvariant();
        }

        private static RelayServerEndpoint PickEndpoint(List<RelayServerEndpoint> eps)
        {
            if (eps == null || eps.Count == 0) throw new Exception("Relay returned no server endpoints");
            foreach (var e in eps) if (e.ConnectionType == "dtls") return e;
            foreach (var e in eps) if (e.ConnectionType == "udp") return e;
            return eps[0];
        }

        private void CreateDriver(ref Unity.Networking.Transport.Relay.RelayServerData data)
        {
            var settings = new NetworkSettings();
            settings.WithRelayParameters(ref data);
            settings.WithFragmentationStageParameters(payloadCapacity: 32 * 1024);
            settings.WithReliableStageParameters(windowSize: 64);
            _driver = NetworkDriver.Create(settings);
            _reliable = _driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
            _unreliable = _driver.CreatePipeline(typeof(FragmentationPipelineStage));
        }

        public void StopNetwork()
        {
            if (_driver.IsCreated)
            {
                try
                {
                    if (role == BrawlNetRole.Host) foreach (var p in Players) if (p.conn.IsCreated) _driver.Disconnect(p.conn);
                    if (_toHost.IsCreated) _driver.Disconnect(_toHost);
                    _driver.ScheduleUpdate().Complete();
                }
                catch { }
                _driver.Dispose();
            }
            _driver = default;
            _toHost = default;
            Players.Clear();
            _pending.Clear();
            Ready = false;
        }

        private void OnDestroy()
        {
            _destroyed = true;
            StopNetwork();
        }

        // ------------------------------------------------------------------ pump
        private void Update()
        {
            if (!_driver.IsCreated) return;
            _driver.ScheduleUpdate().Complete();

            if (role == BrawlNetRole.Host)
            {
                NetworkConnection c;
                while ((c = _driver.Accept()) != default) _pending.Add(c);

                NetworkEvent.Type t;
                while ((t = _driver.PopEvent(out NetworkConnection conn, out DataStreamReader reader)) != NetworkEvent.Type.Empty)
                {
                    if (t == NetworkEvent.Type.Data) HostReceive(conn, ReadAll(ref reader));
                    else if (t == NetworkEvent.Type.Disconnect) HostDrop(conn);
                }
            }
            else if (role == BrawlNetRole.Client)
            {
                NetworkEvent.Type t;
                while ((t = _toHost.PopEvent(_driver, out DataStreamReader reader)) != NetworkEvent.Type.Empty)
                {
                    if (t == NetworkEvent.Type.Connect)
                    {
                        Status = "CONNECTED. WAITING FOR THE HOST...";
                        SendHello();
                    }
                    else if (t == NetworkEvent.Type.Data) ClientReceive(ReadAll(ref reader));
                    else if (t == NetworkEvent.Type.Disconnect)
                    {
                        Ready = false;
                        Status = "DISCONNECTED FROM THE HOST. PRESS ESC";
                        _toHost = default;
                        Disconnected?.Invoke();
                    }
                }

                _intentAccum += Time.unscaledDeltaTime;
                if (Ready && LocalGhost != null && _intentAccum >= 1f / 60f)
                {
                    _intentAccum = 0f;
                    SendIntent(LocalGhost);
                }
            }
        }

        private static byte[] ReadAll(ref DataStreamReader reader)
        {
            int n = reader.Length;
            var arr = new NativeArray<byte>(n, Allocator.Temp);
            reader.ReadBytes(arr);
            byte[] b = arr.ToArray();
            arr.Dispose();
            return b;
        }

        // ------------------------------------------------------------------ host side
        private void HostReceive(NetworkConnection conn, byte[] data)
        {
            if (!Unwrap(data, out byte type, out BinaryReader br)) return;
            using (br)
            {
                if (type == PHello)
                {
                    if (FindPlayer(conn) != null) return;
                    if (Players.Count >= MaxRemotePlayers) { _driver.Disconnect(conn); return; }
                    PilotProfile pilot = null;
                    try { pilot = JsonUtility.FromJson<PilotProfile>(br.ReadString()); } catch { }
                    if (pilot == null) pilot = new PilotProfile { name = "FRIEND" };
                    if (pilot.loadout == null) pilot.loadout = IronFauna.Clamshell.MechLoadout.Default();
                    pilot.name = string.IsNullOrWhiteSpace(pilot.name) ? "FRIEND" : pilot.name.Trim().ToUpperInvariant();
                    if (pilot.name.Length > 16) pilot.name = pilot.name.Substring(0, 16);
                    var p = new RemotePlayer { id = _nextId++, conn = conn, pilot = pilot, lastSeen = Time.unscaledTime };
                    Players.Add(p);
                    _pending.Remove(conn);
                    SendTo(p, PWelcome, w => w.Write(p.id), true);
                    Debug.Log($"[IronFauna Net] {pilot.name} joined (id {p.id})");
                    PlayerJoined?.Invoke(p);
                }
                else if (type == PIntent)
                {
                    var p = FindPlayer(conn);
                    if (p == null) return;
                    p.lastSeen = Time.unscaledTime;
                    p.move = new Vector2(br.ReadSingle(), br.ReadSingle());
                    byte f = br.ReadByte();
                    p.guard = (f & 1) != 0; p.sprint = (f & 2) != 0; p.grab = (f & 4) != 0; p.crouch = (f & 8) != 0; p.sit = (f & 16) != 0;
                    p.lastPunch = (PunchType)br.ReadByte();
                    p.punchSerial = br.ReadInt32();
                    p.lastSlip = br.ReadSByte();
                    p.slipSerial = br.ReadInt32();
                    p.camYaw = br.ReadSingle();
                }
            }
        }

        private void HostDrop(NetworkConnection conn)
        {
            _pending.Remove(conn);
            var p = FindPlayer(conn);
            if (p == null) return;
            Players.Remove(p);
            if (p.camProxy != null) Destroy(p.camProxy.gameObject);
            Debug.Log($"[IronFauna Net] {p.pilot.name} left");
            PlayerLeft?.Invoke(p);
        }

        private RemotePlayer FindPlayer(NetworkConnection conn)
        {
            foreach (var p in Players) if (p.conn == conn) return p;
            return null;
        }

        /// <summary>Host: drives a friend's robot with the input they sent. Presses (punches, slips) are applied once each.</summary>
        public void ApplyIntent(RemotePlayer p, MechBoxer ghost)
        {
            if (p == null || ghost == null) return;
            if (p.camProxy == null)
            {
                p.camProxy = new GameObject($"NetCam_{p.id}").transform;
                p.camProxy.SetParent(transform, false);
            }
            p.camProxy.rotation = Quaternion.Euler(0f, p.camYaw, 0f);
            ghost.moveReference = p.camProxy;
            ghost.lockOn = false;
            var i = ghost.intent;
            i.move = p.move;
            i.guard = p.guard;
            i.sprint = p.sprint;
            i.grab = p.grab;
            i.crouch = p.crouch;
            i.sit = p.sit;
            if (p.appliedPunch < 0) p.appliedPunch = p.punchSerial;
            if (p.appliedSlip < 0) p.appliedSlip = p.slipSerial;
            if (p.punchSerial != p.appliedPunch)
            {
                ghost.mashCount += Mathf.Clamp(p.punchSerial - p.appliedPunch, 1, 10);
                p.appliedPunch = p.punchSerial;
                if (p.lastPunch != PunchType.None) i.punch = p.lastPunch;
            }
            if (p.slipSerial != p.appliedSlip)
            {
                ghost.mashCount += Mathf.Clamp(p.slipSerial - p.appliedSlip, 1, 10);
                p.appliedSlip = p.slipSerial;
                if (p.lastSlip != 0) i.slip = p.lastSlip;
            }
            ghost.intent = i;
        }

        // ------------------------------------------------------------------ client side
        private void SendHello()
        {
            var pilot = PilotStore.ForSlot(0);
            string json = pilot != null ? JsonUtility.ToJson(pilot) : JsonUtility.ToJson(new PilotProfile { name = "FRIEND" });
            SendToHost(PHello, w => w.Write(json), true);
        }

        private void SendIntent(MechBoxer g)
        {
            var i = g.intent;
            SendToHost(PIntent, w =>
            {
                w.Write(i.move.x);
                w.Write(i.move.y);
                byte f = 0;
                if (i.guard) f |= 1;
                if (i.sprint) f |= 2;
                if (i.grab) f |= 4;
                if (i.crouch) f |= 8;
                if (i.sit) f |= 16;
                w.Write(f);
                w.Write((byte)g.netLastPunch);
                w.Write(g.netPunchSerial);
                w.Write((sbyte)g.netLastSlip);
                w.Write(g.netSlipSerial);
                w.Write(g.moveReference != null ? g.moveReference.eulerAngles.y : g.transform.eulerAngles.y);
            }, false);
        }

        private void ClientReceive(byte[] data)
        {
            if (!Unwrap(data, out byte type, out BinaryReader br)) return;
            using (br)
            {
                if (type == PWelcome)
                {
                    MyClientId = br.ReadInt32();
                    Ready = true;
                    Status = $"ONLINE  //  ROOM {JoinCode}";
                    return;
                }
                int pos = (int)br.BaseStream.Position;
                byte[] body = new byte[data.Length - pos];
                Array.Copy(data, pos, body, 0, body.Length);
                HostPacket?.Invoke(type, body);
            }
        }

        // ------------------------------------------------------------------ sending
        private static byte[] Wrap(byte type, Action<BinaryWriter> write)
        {
            using (var ms = new MemoryStream(256))
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(Magic);
                bw.Write(type);
                write?.Invoke(bw);
                bw.Flush();
                return ms.ToArray();
            }
        }

        private static bool Unwrap(byte[] data, out byte type, out BinaryReader br)
        {
            type = 0;
            br = null;
            if (data == null || data.Length < 5) return false;
            br = new BinaryReader(new MemoryStream(data));
            if (br.ReadUInt32() != Magic) { br.Dispose(); br = null; return false; }
            type = br.ReadByte();
            return true;
        }

        private bool SendRaw(NetworkConnection c, bool reliable, byte[] bytes)
        {
            if (!_driver.IsCreated || !c.IsCreated) return false;
            int status = _driver.BeginSend(reliable ? _reliable : _unreliable, c, out DataStreamWriter w, bytes.Length);
            if (status < 0) return false;
            var arr = new NativeArray<byte>(bytes, Allocator.Temp);
            w.WriteBytes(arr);
            arr.Dispose();
            if (w.HasFailedWrites)
            {
                _driver.AbortSend(w);
                return false;
            }
            return _driver.EndSend(w) >= 0;
        }

        public void SendToHost(byte type, Action<BinaryWriter> write, bool reliable)
        {
            if (role != BrawlNetRole.Client) return;
            SendRaw(_toHost, reliable, Wrap(type, write));
        }

        public void SendTo(RemotePlayer p, byte type, Action<BinaryWriter> write, bool reliable)
        {
            if (role != BrawlNetRole.Host || p == null) return;
            SendRaw(p.conn, reliable, Wrap(type, write));
        }

        public void SendToAll(byte type, Action<BinaryWriter> write, bool reliable)
        {
            if (role != BrawlNetRole.Host || Players.Count == 0) return;
            byte[] bytes = Wrap(type, write);
            foreach (var p in Players) SendRaw(p.conn, reliable, bytes);
        }
    }
}
