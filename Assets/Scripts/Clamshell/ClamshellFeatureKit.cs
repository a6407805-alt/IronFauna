using UnityEngine;
using IronFauna.Core;

namespace IronFauna.Clamshell
{
    /// <summary>Spawns a mesh part (MeshFilter + MeshRenderer) with a cached palette material.</summary>
    public static class ClamshellParts
    {
        public static GameObject Spawn(Transform parent, string name, Mesh mesh, Vector3 localPos, Quaternion localRot, Color color, bool emissive = false)
        {
            var go = new GameObject(name);
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSaveInEditor;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = IronFaunaMaterials.Get(color, emissive);
            return go;
        }

        public static Transform Empty(Transform parent, string name, Vector3 localPos, Quaternion localRot)
        {
            var go = new GameObject(name);
            if (!Application.isPlaying) go.hideFlags = HideFlags.DontSaveInEditor;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            return go.transform;
        }
    }

    /// <summary>
    /// Builds the set-specific surface details ("greebles") for one clamshell half.
    /// All details sit on the half's outer face, inside a band along the bone, and never rise more than
    /// 70% of the plate depth, so the joint-clearance rules verified for the bare shells still hold.
    /// Coordinates: local +Y along the bone, local Z outward (multiplied by sign), local X across the plate.
    /// </summary>
    public static class ClamshellFeatureKit
    {
        public struct Frame
        {
            /// <summary>Plate width (X).</summary>
            public float width;
            /// <summary>Shell length along the bone (Y).</summary>
            public float length;
            /// <summary>Plate depth; details may rise up to 0.7 x depth above the outer face.</summary>
            public float depth;
            /// <summary>Distance from the half's local origin to its outer face along Z.</summary>
            public float outer;
            /// <summary>+1 for front/outer halves, -1 for back/inner halves.</summary>
            public float sign;
            /// <summary>Detail band along the bone, as fractions of length (e.g. -0.35..0.35).</summary>
            public float bandLo;
            public float bandHi;
            /// <summary>Shell paint colour for details that are "part of the plate" (ERA bricks, pods).</summary>
            public Color shellColor;
        }

        public static void Build(Transform half, ClamshellSetArchetype set, bool isFront, Frame f)
        {
            var k = new Kit(half, f);
            switch (set)
            {
                case ClamshellSetArchetype.Set01_Ironclad: if (isFront) k.IroncladFront(); else k.IroncladBack(); break;
                case ClamshellSetArchetype.Set02_Bastion: if (isFront) k.BastionFront(); else k.BastionBack(); break;
                case ClamshellSetArchetype.Set03_Overclock: if (isFront) k.OverclockFront(); else k.OverclockBack(); break;
                case ClamshellSetArchetype.Set04_Ripjaw: if (isFront) k.RipjawFront(); else k.RipjawBack(); break;
                case ClamshellSetArchetype.Set05_Afterburn: if (isFront) k.AfterburnFront(); else k.AfterburnBack(); break;
                case ClamshellSetArchetype.Set06_Voltaic: if (isFront) k.VoltaicFront(); else k.VoltaicBack(); break;
                case ClamshellSetArchetype.Set07_Salvo: if (isFront) k.SalvoFront(); else k.SalvoBack(); break;
                case ClamshellSetArchetype.Set08_Goliath: if (isFront) k.GoliathFront(); else k.GoliathBack(); break;
                case ClamshellSetArchetype.Set09_Mantis: if (isFront) k.MantisFront(); else k.MantisBack(); break;
                case ClamshellSetArchetype.Set10_Aegis: if (isFront) k.AegisFront(); else k.AegisBack(); break;
            }
        }

        /// <summary>Helper that knows the half's frame and converts "height above the outer face" into local Z.</summary>
        private struct Kit
        {
            private readonly Transform _p;
            private readonly Frame _f;
            private readonly float _W;
            private readonly float _yc;
            private readonly float _sp;
            private readonly float _h;

            public Kit(Transform parent, Frame f)
            {
                _p = parent;
                _f = f;
                _W = f.width;
                _yc = (f.bandLo + f.bandHi) * 0.5f * f.length;
                _sp = Mathf.Abs(f.bandHi - f.bandLo) * f.length;
                _h = f.depth * 0.7f;
            }

            // Position of a detail whose centre is `height` above the outer face.
            private Vector3 P(float x, float y, float height)
            {
                return new Vector3(x, y, _f.sign * (_f.outer + height));
            }

            // Rotation for meshes built "sitting on z = 0, pointing +Z" so they point outward on back halves too.
            private Quaternion Out => _f.sign > 0f ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
            private static readonly Quaternion AlongZ = Quaternion.Euler(90f, 0f, 0f);
            private static readonly Quaternion AlongX = Quaternion.Euler(0f, 0f, 90f);

            private void Box(string n, float x, float y, float sx, float sy, float sz, float baseHeight, Color c, bool emissive = false)
            {
                var mesh = ClamshellMeshFactory.BeveledBox(new Vector3(sx, sy, sz), Mathf.Min(sx, Mathf.Min(sy, sz)) * 0.18f);
                ClamshellParts.Spawn(_p, n, mesh, P(x, y, baseHeight + sz * 0.5f), Quaternion.identity, c, emissive);
            }

            private void Bolt(float x, float y, float baseHeight)
            {
                float r = _W * 0.035f;
                var mesh = ClamshellMeshFactory.Prism(r, _h * 0.22f, 6);
                ClamshellParts.Spawn(_p, "HexBolt", mesh, P(x, y, baseHeight + _h * 0.11f), AlongZ, IronFaunaPalette.DarkIron);
            }

            // ---------------------------------------------------------- 01 Ironclad
            public void IroncladFront()
            {
                Box("OffWhiteStripe", _W * 0.22f, _yc, _W * 0.14f, _sp * 0.9f, _h * 0.3f, 0f, IronFaunaPalette.OffWhitePanel);
                for (int i = 0; i < 4; i++)
                {
                    float x = (i % 2 == 0 ? -1f : 1f) * _W * 0.3f;
                    float y = _yc + (i < 2 ? -1f : 1f) * _sp * 0.42f;
                    Bolt(x, y, 0f);
                }
            }

            public void IroncladBack()
            {
                float y = _yc - _sp * 0.28f;
                for (int i = 0; i < 5; i++)
                {
                    float x = (-2 + i) * _W * 0.14f;
                    Color c = i % 2 == 0 ? IronFaunaPalette.HazardYellow : IronFaunaPalette.DarkIron;
                    Box("HazardBlock", x, y, _W * 0.14f, _sp * 0.24f, _h * 0.3f, 0f, c);
                }
            }

            // ---------------------------------------------------------- 02 Bastion
            public void BastionFront()
            {
                for (int i = -1; i <= 1; i += 2)
                {
                    float y = _yc + i * _sp * 0.25f;
                    Box("ERABrick", 0f, y, _W * 0.78f, _sp * 0.44f, _h * 0.8f, 0f, _f.shellColor);
                    Bolt(-_W * 0.26f, y, _h * 0.8f - _h * 0.05f);
                    Bolt(_W * 0.26f, y, _h * 0.8f - _h * 0.05f);
                }
            }

            public void BastionBack()
            {
                Box("AmmoDrum", 0f, _yc, _W * 0.7f, _sp * 0.85f, _h * 0.8f, 0f, IronFaunaPalette.GunmetalFrame);
                Box("FeedHatch", 0f, _yc + _sp * 0.15f, _W * 0.4f, _sp * 0.22f, _h * 0.15f, _h * 0.8f, IronFaunaPalette.OffWhitePanel);
            }

            // ---------------------------------------------------------- 03 Overclock
            public void OverclockFront()
            {
                Box("ConduitSlot", 0f, _yc, _W * 0.34f, _sp * 0.95f, _h * 0.25f, 0f, IronFaunaPalette.DarkIron);
                float r = Mathf.Min(_W * 0.09f, _h * 0.35f);
                var pipe = ClamshellMeshFactory.Prism(r, _sp * 0.88f, 8);
                ClamshellParts.Spawn(_p, "CyanConduit", pipe, P(0f, _yc, _h * 0.25f + r), Quaternion.identity, IronFaunaPalette.CyanEnergy, true);
            }

            public void OverclockBack()
            {
                Box("RadiatorHousing", 0f, _yc, _W * 0.8f, _sp, _h * 0.25f, 0f, IronFaunaPalette.DarkIron);
                var fin = ClamshellMeshFactory.BeveledBox(new Vector3(_W * 0.74f, _sp * 0.05f, _h * 0.6f), _sp * 0.01f);
                for (int i = 0; i < 5; i++)
                {
                    float y = _yc - _sp * 0.4f + i * _sp * 0.2f;
                    ClamshellParts.Spawn(_p, "RadiatorFin", fin, P(0f, y, _h * 0.25f + _h * 0.3f), Quaternion.Euler(-25f * _f.sign, 0f, 0f), IronFaunaPalette.CyanEnergy, true);
                }
            }

            // ---------------------------------------------------------- 04 Ripjaw
            public void RipjawFront()
            {
                float toothLen = _sp / 4f;
                var tooth = ClamshellMeshFactory.Ridge(_W * 0.3f, _h, toothLen, 1f, 0.05f);
                for (int i = 0; i < 4; i++)
                {
                    float y = _yc - _sp * 0.5f + toothLen * (i + 0.5f);
                    ClamshellParts.Spawn(_p, "SerratedCleat", tooth, P(0f, y, 0f), Out, IronFaunaPalette.GunmetalFrame);
                }
            }

            public void RipjawBack()
            {
                float r = Mathf.Min(_W * 0.2f, _h * 0.5f);
                var can = ClamshellMeshFactory.Prism(r, _sp * 0.8f, 10);
                ClamshellParts.Spawn(_p, "HydraulicCanister", can, P(0f, _yc, r), Quaternion.identity, IronFaunaPalette.GunmetalFrame);
                var band = ClamshellMeshFactory.Prism(r * 1.08f, _sp * 0.12f, 10);
                ClamshellParts.Spawn(_p, "HazardBand", band, P(0f, _yc + _sp * 0.18f, r), Quaternion.identity, IronFaunaPalette.HazardYellow);
            }

            // ---------------------------------------------------------- 05 Afterburn
            public void AfterburnFront()
            {
                var scoop = ClamshellMeshFactory.Wedge(_W * 0.7f, _h, _sp * 0.8f, 0.15f);
                ClamshellParts.Spawn(_p, "RamAirScoop", scoop, P(0f, _yc, 0f), Out, _f.shellColor);
                Box("IntakeMouth", 0f, _yc + _sp * 0.4f, _W * 0.56f, _sp * 0.05f, _h * 0.7f, _h * 0.1f, IronFaunaPalette.DarkIron);
            }

            public void AfterburnBack()
            {
                float r = Mathf.Min(_W * 0.3f, _h * 0.5f);
                var nozzle = ClamshellMeshFactory.TaperedPrism(r, r * 0.7f, _sp * 0.6f, 8);
                ClamshellParts.Spawn(_p, "VectorNozzle", nozzle, P(0f, _yc, r), Quaternion.identity, IronFaunaPalette.DarkIron);
                var glow = ClamshellMeshFactory.Prism(r * 0.7f, _sp * 0.05f, 8);
                ClamshellParts.Spawn(_p, "NozzleGlow", glow, P(0f, _yc - _sp * 0.31f, r), Quaternion.identity, IronFaunaPalette.OrangeJointCore, true);
            }

            // ---------------------------------------------------------- 06 Voltaic
            public void VoltaicFront()
            {
                float r = Mathf.Min(_sp * 0.1f, _h * 0.5f);
                var coil = ClamshellMeshFactory.Prism(r, _W * 0.85f, 10);
                for (int i = -1; i <= 1; i += 2)
                {
                    ClamshellParts.Spawn(_p, "CopperCoil", coil, P(0f, _yc + i * _sp * 0.24f, r), AlongX, IronFaunaPalette.CopperCoil);
                }
                Box("ArcNode", 0f, _yc, _W * 0.18f, _sp * 0.18f, _h * 0.8f, 0f, IronFaunaPalette.CyanEnergy, true);
            }

            public void VoltaicBack()
            {
                for (int i = -1; i <= 1; i += 2)
                {
                    float x = i * _W * 0.2f;
                    Box("CapacitorBank", x, _yc, _W * 0.32f, _sp * 0.85f, _h * 0.75f, 0f, IronFaunaPalette.GunmetalFrame);
                    Box("CapacitorCap", x, _yc + _sp * 0.32f, _W * 0.26f, _sp * 0.1f, _h * 0.15f, _h * 0.75f, IronFaunaPalette.CyanEnergy, true);
                }
            }

            // ---------------------------------------------------------- 07 Salvo
            public void SalvoFront()
            {
                Box("MissilePod", 0f, _yc, _W * 0.8f, _sp * 0.85f, _h * 0.72f, 0f, _f.shellColor);
                float r = Mathf.Min(_W * 0.07f, _sp * 0.08f);
                var tube = ClamshellMeshFactory.Prism(r, _h * 0.12f, 8);
                for (int ix = -1; ix <= 1; ix++)
                {
                    for (int iy = -1; iy <= 1; iy += 2)
                    {
                        ClamshellParts.Spawn(_p, "MissileTube", tube, P(ix * _W * 0.22f, _yc + iy * _sp * 0.18f, _h * 0.72f + _h * 0.06f), AlongZ, IronFaunaPalette.DarkIron);
                    }
                }
            }

            public void SalvoBack()
            {
                Box("FlareDispenser", 0f, _yc, _W * 0.6f, _sp * 0.7f, _h * 0.7f, 0f, IronFaunaPalette.GunmetalFrame);
                for (int i = 0; i < 4; i++)
                {
                    float y = _yc - _sp * 0.24f + i * _sp * 0.16f;
                    Box("FlareSlot", 0f, y, _W * 0.45f, _sp * 0.05f, _h * 0.1f, _h * 0.7f, IronFaunaPalette.DarkIron);
                }
            }

            // ---------------------------------------------------------- 08 Goliath
            public void GoliathFront()
            {
                Box("PileDriverBlock", 0f, _yc + _sp * 0.12f, _W * 0.86f, _sp * 0.68f, _h * 0.85f, 0f, _f.shellColor);
                Box("HazardChevron", 0f, _yc + _sp * 0.12f, _W * 0.6f, _sp * 0.12f, _h * 0.12f, _h * 0.85f, IronFaunaPalette.HazardYellow);
                float r = Mathf.Min(_W * 0.14f, _h * 0.5f);
                var piston = ClamshellMeshFactory.Prism(r, _sp * 0.28f, 8);
                ClamshellParts.Spawn(_p, "RamPiston", piston, P(0f, _yc - _sp * 0.34f, r), Quaternion.identity, IronFaunaPalette.GunmetalFrame);
            }

            public void GoliathBack()
            {
                float r = Mathf.Min(_W * 0.2f, _h * 0.5f);
                var weight = ClamshellMeshFactory.Prism(r, _sp * 0.8f, 10);
                for (int i = -1; i <= 1; i += 2)
                {
                    ClamshellParts.Spawn(_p, "Counterweight", weight, P(i * _W * 0.22f, _yc, r), Quaternion.identity, IronFaunaPalette.DarkIron);
                }
            }

            // ---------------------------------------------------------- 09 Mantis
            public void MantisFront()
            {
                var facet = ClamshellMeshFactory.FacetPrism(_W * 0.82f, _h, _sp);
                ClamshellParts.Spawn(_p, "StealthPrism", facet, P(0f, _yc, 0f), Out, _f.shellColor);
            }

            public void MantisBack()
            {
                Box("ExhaustShroud", 0f, _yc - _sp * 0.1f, _W * 0.7f, _sp * 0.6f, _h * 0.4f, 0f, IronFaunaPalette.GunmetalFrame);
                for (int i = 0; i < 3; i++)
                {
                    Box("BaffleLouver", 0f, _yc - _sp * 0.28f + i * _sp * 0.18f, _W * 0.6f, _sp * 0.05f, _h * 0.08f, _h * 0.4f, IronFaunaPalette.DarkIron);
                }
                var fin = ClamshellMeshFactory.Ridge(_W * 0.06f, _h * 0.55f, _sp * 0.4f, 1f, 0.2f);
                ClamshellParts.Spawn(_p, "ECMFin", fin, P(0f, _yc + _sp * 0.28f, _h * 0.4f - _h * 0.05f), Out, IronFaunaPalette.OffWhitePanel);
            }

            // ---------------------------------------------------------- 10 Aegis
            public void AegisFront()
            {
                Box("EmitterFrame", 0f, _yc, _W * 0.52f, _sp * 0.95f, _h * 0.3f, 0f, IronFaunaPalette.DarkIron);
                for (int i = -1; i <= 1; i++)
                {
                    Box("HardLightRail", i * _W * 0.14f, _yc, _W * 0.08f, _sp * 0.85f, _h * 0.3f, _h * 0.3f, IronFaunaPalette.CyanEnergy, true);
                }
            }

            public void AegisBack()
            {
                Box("FieldInverterPack", 0f, _yc, _W * 0.7f, _sp * 0.7f, _h * 0.6f, 0f, IronFaunaPalette.GunmetalFrame);
                float r = Mathf.Min(_sp * 0.08f, _h * 0.2f);
                var coil = ClamshellMeshFactory.Prism(r, _W * 0.75f, 10);
                ClamshellParts.Spawn(_p, "InverterCoil", coil, P(0f, _yc, _h * 0.6f + r), AlongX, IronFaunaPalette.CyanEnergy, true);
            }
        }
    }
}
