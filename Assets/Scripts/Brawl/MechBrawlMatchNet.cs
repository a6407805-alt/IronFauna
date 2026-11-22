using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using IronFauna.Boxing;
using IronFauna.Clamshell;

namespace IronFauna.Brawl
{
    /// <summary>
    /// Online half of the match (Unity Relay, host-authoritative):
    ///  - Host: friends' pilots join the roster (their own robot, upgrades and chips), their input drives their robot,
    ///    every new round sends the arena seed + roster, and ~20 times a second every robot's pose and status.
    ///  - Client: rebuilds the same arena and robots, shows every robot exactly where the host has it (puppets),
    ///    sends its own input, and saves its pilot's SCRAP / XP when the host reports the round.
    /// </summary>
    public partial class MechBrawlMatch
    {
        private bool NetHost => _net != null && config.netRole == BrawlNetRole.Host;
        private bool NetClient => _net != null && config.netRole == BrawlNetRole.Client;

        private Text _netStatus;
        private bool _restartForJoin;
        private float _snapAccum;
        private int _netRound;
        private int _clientRound = -1;
        private readonly List<Fighter> _netOrder = new List<Fighter>();   // client: fighters in the host's order

        private const float SnapshotInterval = 1f / 20f;

        private void HookNetwork()
        {
            _net.PlayerJoined += p =>
            {
                Banner($"{p.pilot.name} JOINED THE FIGHT", 2.2f);
                _restartForJoin = true;   // rebuild the round so the friend gets a robot
            };
            _net.PlayerLeft += p =>
            {
                foreach (var f in _fighters)
                {
                    if (f.remote != p) continue;
                    f.remote = null;
                    f.pilot = null;
                    var ai = f.ghost != null ? f.ghost.GetComponent<MechBoxerAI>() : null;
                    if (ai != null) ai.enabled = true;   // a bot takes over the robot
                }
                Banner($"{p.pilot.name} LEFT", 2f);
            };
            _net.HostPacket += OnHostPacket;
            _net.Disconnected += () => Banner("HOST LEFT THE GAME", 4f, false);
        }

        private void PersonalBanner(Fighter f, string text, float seconds)
        {
            if (f == null) return;
            if (f.player) Banner(text, seconds, false);
            else if (f.remote != null && NetHost)
                _net.SendTo(f.remote, MechBrawlNet.PBanner, w => { w.Write(text); w.Write(seconds); }, true);
        }

        private void TickNetStatus()
        {
            if (_netStatus == null) return;
            bool online = _net != null && config.netRole != BrawlNetRole.Offline;
            _netStatus.gameObject.SetActive(online);
            if (!online) return;
            string s = _net.Status;
            if (NetHost && _net.Ready) s = $"JOIN CODE  {_net.JoinCode}   //   {_net.Players.Count + 1}/8 ONLINE";
            _netStatus.text = s;
            _netStatus.color = _net.Failed ? IronFaunaPalette.CrimsonFront : IronFaunaPalette.UiAccent;
        }

        // ------------------------------------------------------------------ host
        private void HostNetTick()
        {
            foreach (var f in _fighters)
                if (f.remote != null && f.ghost != null && !f.out_) _net.ApplyIntent(f.remote, f.ghost);

            _snapAccum += Time.unscaledDeltaTime;
            if (_snapAccum < SnapshotInterval) return;
            _snapAccum = 0f;
            if (_net.Players.Count == 0 || _fighters.Count == 0) return;
            int round = _netRound;
            _net.SendToAll(MechBrawlNet.PSnapshot, w =>
            {
                w.Write(round);
                w.Write((byte)_fighters.Count);
                foreach (var f in _fighters)
                {
                    var g = f.ghost;
                    Vector3 gp = g != null ? g.transform.position : Vector3.zero;
                    w.Write(gp.x); w.Write(gp.y); w.Write(gp.z);
                    w.Write(g != null ? g.transform.eulerAngles.y : 0f);
                    w.Write(f.out_);
                    w.Write((byte)Mathf.RoundToInt((g != null ? g.Stamina01 : 1f) * 255f));
                    f.ragdoll.WriteNetStatus(w);
                    f.ragdoll.WriteNetPose(w);
                }
            }, false);
        }

        /// <summary>Host: the arena seed and the roster of this round, so friends build exactly the same fight.</summary>
        private void BroadcastSetup()
        {
            _netRound++;
            if (_net.Players.Count == 0) return;
            int round = _netRound;
            _net.SendToAll(MechBrawlNet.PSetup, w =>
            {
                w.Write(round);
                w.Write((byte)config.mode);
                w.Write((byte)config.stage);
                w.Write(_arenaSeed);
                w.Write((byte)_fighters.Count);
                foreach (var f in _fighters)
                {
                    w.Write(f.name ?? "");
                    w.Write(JsonUtility.ToJson(f.loadout ?? MechLoadout.Default()));
                    w.Write((byte)f.team);
                    w.Write(f.isBoss);
                    w.Write(f.hpMult);
                    w.Write(f.scale);
                    w.Write(f.spawnPos.x); w.Write(f.spawnPos.y); w.Write(f.spawnPos.z);
                    w.Write(f.spawnYaw);
                    w.Write(f.remote != null ? f.remote.id : -1);
                }
            }, true);
        }

        // ------------------------------------------------------------------ client
        private void OnHostPacket(byte type, byte[] body)
        {
            if (!NetClient) return;
            using (var r = new BinaryReader(new MemoryStream(body)))
            {
                try
                {
                    switch (type)
                    {
                        case MechBrawlNet.PSetup: ClientBuildRound(r); break;
                        case MechBrawlNet.PSnapshot: ClientApplySnapshot(r); break;
                        case MechBrawlNet.PBanner: Banner(r.ReadString(), r.ReadSingle(), false); break;
                        case MechBrawlNet.PHit: ClientShowHit(r); break;
                        case MechBrawlNet.PResult: ClientRoundResult(r); break;
                    }
                }
                catch (EndOfStreamException)
                {
                    Debug.LogWarning($"[IronFauna Net] Short packet (type {type}) ignored");
                }
            }
        }

        private void ClientBuildRound(BinaryReader r)
        {
            int round = r.ReadInt32();
            config.mode = (BrawlGameMode)r.ReadByte();
            config.stage = (BrawlStageId)r.ReadByte();
            int seed = r.ReadInt32();
            int count = r.ReadByte();

            _lastHitBy.Clear();
            foreach (var f in _fighters)
            {
                if (f.physGo != null) Destroy(f.physGo);
                if (f.ghostGo != null) Destroy(f.ghostGo);
            }
            _fighters.Clear();
            _ragdollList.Clear();
            _netOrder.Clear();
            _input = null;
            if (_arena != null) Destroy(_arena.gameObject);
            _resetT = -1f;

            BuildArena(config.stage, seed);
            SetupSplitScreenCameras(1);
            Fighter mine = null;
            for (int i = 0; i < count; i++)
            {
                string name = r.ReadString();
                string json = r.ReadString();
                int team = r.ReadByte();
                bool boss = r.ReadBoolean();
                float hpMult = r.ReadSingle();
                float scale = r.ReadSingle();
                var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                float yaw = r.ReadSingle();
                int owner = r.ReadInt32();

                MechLoadout l = null;
                try { l = JsonUtility.FromJson<MechLoadout>(json); } catch { }
                if (l == null) l = MechLoadout.Default();
                bool isMine = owner >= 0 && owner == _net.MyClientId;
                Camera cam = isMine && _playerCams.Count > 0 ? _playerCams[0].GetComponent<Camera>() : null;
                var f = Spawn(name, l, pos, yaw, isMine, isMine ? 0 : -1, team, boss, hpMult, cam, null, scale);
                f.ownerId = owner;
                var ai = f.ghost.GetComponent<MechBoxerAI>();
                if (ai != null) ai.enabled = false;
                f.ragdoll.EnterNetPuppet();
                _netOrder.Add(f);
                if (isMine) mine = f;
            }
            // "me" is always _fighters[0] (HUD, camera, hit feedback)
            if (mine != null) _fighters.Add(mine);
            foreach (var f in _netOrder) if (f != mine) _fighters.Add(f);
            foreach (var f in _fighters) _ragdollList.Add(f.ragdoll);
            var first = _fighters.Count > 0 ? _fighters[0] : null;
            if (_playerCams.Count > 0 && first != null)
            {
                _playerCams[0].player = first.ragdoll;
                _playerCams[0].Snap();
            }
            _net.LocalGhost = mine != null ? mine.ghost : null;
            _clientRound = round;
            Retarget();
            Banner(mine == null ? "SPECTATING: ARENA FULL" : BrawlTerrain.StageName(config.stage), 1.8f, false);
            if (_announcer != null) _announcer.OnRoundStart(config.stage, config.mode, "");
        }

        private void ClientApplySnapshot(BinaryReader r)
        {
            int round = r.ReadInt32();
            if (round != _clientRound) return;   // a snapshot from the previous round
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
            {
                var gp = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                float yaw = r.ReadSingle();
                bool out_ = r.ReadBoolean();
                float stamina01 = r.ReadByte() / 255f;
                if (i >= _netOrder.Count) return;   // roster mismatch: wait for the next round setup
                var f = _netOrder[i];
                bool wasKo = f.ragdoll.IsKO;
                f.ragdoll.ReadNetStatus(r);
                f.ragdoll.ReadNetPose(r);
                if (f.ghost != null)
                {
                    f.ghost.Teleport(gp);
                    f.ghost.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    f.ghost.stamina = stamina01 * f.ghost.staminaMax;
                }
                if (f.player && f.ragdoll.IsKO && !wasKo) Banner("K.O.", 2.5f, false);
                if (out_ && !f.out_)
                {
                    f.out_ = true;
                    int rem = 0;
                    foreach (var o in _netOrder) if (!o.out_) rem++;
                    if (_announcer != null) _announcer.OnElimination(f.name, false, rem);
                    Retarget();
                }
                else if (!out_ && f.out_) f.out_ = false;
            }
        }

        private Fighter NetFighter(int index) => index >= 0 && index < _netOrder.Count ? _netOrder[index] : null;

        private void ClientShowHit(BinaryReader r)
        {
            var victim = NetFighter(r.ReadSByte());
            var attacker = NetFighter(r.ReadSByte());
            var point = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            float damage = r.ReadSingle(), power = r.ReadSingle();
            bool ripped = r.ReadBoolean();
            if (victim == null || victim.ragdoll == null) return;
            victim.ragdoll.NetShowHit(attacker != null ? attacker.ragdoll : null, point, damage, power, ripped);
        }

        private void ClientRoundResult(BinaryReader r)
        {
            float damage = r.ReadSingle();
            int kos = r.ReadInt32();
            bool won = r.ReadBoolean();
            var pilot = IronFauna.Pilots.PilotStore.ForSlot(0);
            if (pilot == null) return;
            string line = IronFauna.Pilots.PilotUpgrades.AwardMatch(pilot, damage, kos, won);
            if (line != null && _rewardText != null)
            {
                _rewardText.text = line;
                _rewardT = 5f;
            }
        }

        /// <summary>Client frame: no rules run here (the host decides hits, KOs and rounds), only the HUD.</summary>
        private void ClientUpdate()
        {
            TickNetStatus();
            if (_fighters.Count > 0)
            {
                UpdateMoveLabel();
                TickPopups(Time.unscaledDeltaTime);
                _targetT -= Time.unscaledDeltaTime;
                if (_targetT <= 0f) { _targetT = 0.4f; Retarget(); }
                TickHudExtras();
            }
            TickBannerAndReward();
        }
    }
}
