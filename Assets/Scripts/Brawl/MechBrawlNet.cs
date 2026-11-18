using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using IronFauna.Boxing;

namespace IronFauna.Brawl
{
    public enum BrawlNetRole
    {
        Offline = 0,
        Host = 1,
        Client = 2,
    }

    /// <summary>
    /// Lightweight UDP Host-Authoritative Netcode for up to 8 players over LAN or Internet (UDP port 7777).
    ///  - Host simulates all 8 active-ragdoll mechs, receives clients' MechBoxer.Intent packets, and broadcasts
    ///    compact 8-fighter state snapshots at 30 Hz.
    ///  - Clients send their local input Intent at 60 Hz and smoothly drive remote fighters from Host snapshots.
    /// </summary>
    public class MechBrawlNet : MonoBehaviour
    {
        public const int DefaultPort = 7777;
        private const uint Magic = 0x49464238; // "IFB8"
        private const byte PacketIntent = 1;
        private const byte PacketSnapshot = 2;

        public BrawlNetRole role = BrawlNetRole.Offline;
        public string hostIp = "127.0.0.1";
        public int port = DefaultPort;

        public struct FighterSyncState
        {
            public Vector3 pos;
            public float yaw;
            public Vector2 move;
            public PunchType punch;
            public bool guard, sprint, grab, crouch, sit, isDown, isKO;
            public float health, maxHealth, poise01;
        }

        private class ClientSlot
        {
            public IPEndPoint endpoint;
            public int fighterIndex;
            public float lastSeen;
            public MechBoxer.Intent latestIntent;
        }

        private UdpClient _udp;
        private readonly List<ClientSlot> _clients = new List<ClientSlot>();
        public readonly FighterSyncState[] RemoteStates = new FighterSyncState[8];
        public bool HasSnapshot { get; private set; }
        public int AssignedSlot { get; private set; } = 1;
        public int ConnectedClientCount => _clients.Count;

        private float _sendAccumulator;

        public void StartNetwork(BrawlNetRole netRole, string ip, int netPort = DefaultPort)
        {
            StopNetwork();
            role = netRole;
            hostIp = string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip.Trim();
            port = netPort;
            if (role == BrawlNetRole.Offline) return;

            try
            {
                if (role == BrawlNetRole.Host)
                {
                    _udp = new UdpClient(port);
                    _udp.Client.Blocking = false;
                    Debug.Log($"[IronFauna Net] Hosting 8-player room on UDP :{port}");
                }
                else if (role == BrawlNetRole.Client)
                {
                    _udp = new UdpClient(0);
                    _udp.Client.Blocking = false;
                    Debug.Log($"[IronFauna Net] Joining 8-player host at {hostIp}:{port}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[IronFauna Net] Could not open UDP socket ({ex.Message}). Running offline.");
                role = BrawlNetRole.Offline;
            }
        }

        public void StopNetwork()
        {
            if (_udp != null)
            {
                try { _udp.Close(); } catch { }
                _udp = null;
            }
            _clients.Clear();
            HasSnapshot = false;
        }

        private void OnDestroy() => StopNetwork();

        public void PollNetwork(IList<MechRagdoll> fighters, MechBoxer.Intent localClientIntent)
        {
            if (_udp == null || role == BrawlNetRole.Offline) return;

            // 1. Read all incoming UDP datagrams
            while (_udp.Available > 0)
            {
                try
                {
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udp.Receive(ref remote);
                    if (data == null || data.Length < 6) continue;
                    using (var ms = new MemoryStream(data))
                    using (var br = new BinaryReader(ms))
                    {
                        if (br.ReadUInt32() != Magic) continue;
                        byte type = br.ReadByte();
                        if (role == BrawlNetRole.Host && type == PacketIntent)
                        {
                            ReadClientIntent(remote, br, fighters.Count);
                        }
                        else if (role == BrawlNetRole.Client && type == PacketSnapshot)
                        {
                            ReadHostSnapshot(br);
                        }
                    }
                }
                catch
                {
                    break;
                }
            }

            // 2. Host applies remote client intents & broadcasts 30 Hz state snapshots
            _sendAccumulator += Time.unscaledDeltaTime;
            if (role == BrawlNetRole.Host)
            {
                for (int i = _clients.Count - 1; i >= 0; i--)
                {
                    var c = _clients[i];
                    if (Time.unscaledTime - c.lastSeen > 8f)
                    {
                        _clients.RemoveAt(i);
                        continue;
                    }
                    if (c.fighterIndex >= 0 && c.fighterIndex < fighters.Count && fighters[c.fighterIndex] != null)
                    {
                        var ghost = fighters[c.fighterIndex].ghost;
                        if (ghost != null)
                        {
                            var ai = ghost.GetComponent<MechBoxerAI>();
                            if (ai != null) ai.enabled = false; // remote human takes over this bot slot!
                            ghost.intent = c.latestIntent;
                        }
                    }
                }

                if (_sendAccumulator >= 1f / 30f)
                {
                    _sendAccumulator = 0f;
                    BroadcastSnapshot(fighters);
                }
            }
            else if (role == BrawlNetRole.Client)
            {
                if (_sendAccumulator >= 1f / 60f)
                {
                    _sendAccumulator = 0f;
                    SendIntentToHost(localClientIntent);
                }
            }
        }

        private void ReadClientIntent(IPEndPoint remote, BinaryReader br, int maxFighters)
        {
            ClientSlot slot = null;
            foreach (var c in _clients)
            {
                if (c.endpoint.Equals(remote)) { slot = c; break; }
            }
            if (slot == null && _clients.Count < 7)
            {
                int assigned = Mathf.Clamp(_clients.Count + 1, 1, Mathf.Max(1, maxFighters - 1));
                slot = new ClientSlot { endpoint = remote, fighterIndex = assigned };
                _clients.Add(slot);
            }
            if (slot == null) return;
            slot.lastSeen = Time.unscaledTime;
            var intent = new MechBoxer.Intent
            {
                move = new Vector2(br.ReadSingle(), br.ReadSingle()),
                punch = (PunchType)br.ReadByte(),
                slip = br.ReadSByte(),
            };
            byte flags = br.ReadByte();
            intent.guard = (flags & 1) != 0;
            intent.sprint = (flags & 2) != 0;
            intent.grab = (flags & 4) != 0;
            intent.crouch = (flags & 8) != 0;
            intent.sit = (flags & 16) != 0;
            slot.latestIntent = intent;
        }

        private void SendIntentToHost(MechBoxer.Intent i)
        {
            try
            {
                using (var ms = new MemoryStream(32))
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write(Magic);
                    bw.Write(PacketIntent);
                    bw.Write(i.move.x);
                    bw.Write(i.move.y);
                    bw.Write((byte)i.punch);
                    bw.Write((sbyte)i.slip);
                    byte flags = 0;
                    if (i.guard) flags |= 1;
                    if (i.sprint) flags |= 2;
                    if (i.grab) flags |= 4;
                    if (i.crouch) flags |= 8;
                    if (i.sit) flags |= 16;
                    bw.Write(flags);
                    byte[] bytes = ms.ToArray();
                    _udp.Send(bytes, bytes.Length, hostIp, port);
                }
            }
            catch { }
        }

        private void BroadcastSnapshot(IList<MechRagdoll> fighters)
        {
            if (_clients.Count == 0) return;
            try
            {
                using (var ms = new MemoryStream(256))
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write(Magic);
                    bw.Write(PacketSnapshot);
                    byte count = (byte)Mathf.Min(8, fighters.Count);
                    bw.Write(count);
                    for (int i = 0; i < count; i++)
                    {
                        var r = fighters[i];
                        var g = r != null ? r.ghost : null;
                        Vector3 p = r != null ? r.Position : Vector3.zero;
                        bw.Write(p.x); bw.Write(p.y); bw.Write(p.z);
                        bw.Write(g != null ? g.transform.eulerAngles.y : 0f);
                        bw.Write(g != null ? g.intent.move.x : 0f);
                        bw.Write(g != null ? g.intent.move.y : 0f);
                        bw.Write((byte)(g != null ? g.CurrentPunch : PunchType.None));
                        byte flags = 0;
                        if (g != null && g.intent.guard) flags |= 1;
                        if (g != null && g.intent.sprint) flags |= 2;
                        if (g != null && g.intent.grab) flags |= 4;
                        if (g != null && g.IsCrouching) flags |= 8;
                        if (g != null && g.IsSeated) flags |= 16;
                        if (r != null && r.IsDown) flags |= 32;
                        if (r != null && r.IsKO) flags |= 64;
                        bw.Write(flags);
                        bw.Write(r != null ? r.health : 0f);
                        bw.Write(r != null ? r.maxHealth : 160f);
                        bw.Write(r != null ? r.Poise01 : 1f);
                    }
                    byte[] payload = ms.ToArray();
                    foreach (var c in _clients)
                    {
                        _udp.Send(payload, payload.Length, c.endpoint);
                    }
                }
            }
            catch { }
        }

        private void ReadHostSnapshot(BinaryReader br)
        {
            int count = Mathf.Min(8, (int)br.ReadByte());
            for (int i = 0; i < count; i++)
            {
                var s = new FighterSyncState
                {
                    pos = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                    yaw = br.ReadSingle(),
                    move = new Vector2(br.ReadSingle(), br.ReadSingle()),
                    punch = (PunchType)br.ReadByte(),
                };
                byte flags = br.ReadByte();
                s.guard = (flags & 1) != 0;
                s.sprint = (flags & 2) != 0;
                s.grab = (flags & 4) != 0;
                s.crouch = (flags & 8) != 0;
                s.sit = (flags & 16) != 0;
                s.isDown = (flags & 32) != 0;
                s.isKO = (flags & 64) != 0;
                s.health = br.ReadSingle();
                s.maxHealth = br.ReadSingle();
                s.poise01 = br.ReadSingle();
                RemoteStates[i] = s;
            }
            HasSnapshot = true;
        }
    }
}
