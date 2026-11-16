using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using IronFauna.Core;

namespace IronFauna.Gameplay
{
    /// <summary>
    /// M1-B Grid Defense & Economy Manager (Walls = 30 credits, Auto-Turrets = 150 credits, Starting Budget = 300).
    /// </summary>
    public class GridDefenseManager : MonoBehaviour
    {
        public static GridDefenseManager Instance { get; private set; }

        public int credits = 300;
        public int wallCost = 30;
        public int turretCost = 150;
        public float cellSize = 2.0f;

        private readonly HashSet<Vector2Int> _occupiedCells = new HashSet<Vector2Int>();

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void AddCredits(int amount)
        {
            credits += amount;
            Debug.Log($"[IronFauna] +{amount} Credits! Total Credits: {credits}");
        }

        public bool TryPlaceWall(Vector2Int cell)
        {
            if (_occupiedCells.Contains(cell) || credits < wallCost) return false;
            credits -= wallCost;
            _occupiedCells.Add(cell);

            Vector3 worldPos = new Vector3(cell.x * cellSize, 0.75f, cell.y * cellSize);
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = $"GridWall_{cell.x}_{cell.y}";
            wall.transform.position = worldPos;
            wall.transform.localScale = new Vector3(cellSize * 0.92f, 1.5f, cellSize * 0.92f);
            IronFaunaMaterials.Apply(wall, IronFaunaPalette.GunmetalFrame, false);

            var obstacle = wall.AddComponent<NavMeshObstacle>();
            obstacle.carving = true;
            return true;
        }

        public bool TryPlaceTurret(Vector2Int cell)
        {
            if (_occupiedCells.Contains(cell) || credits < turretCost) return false;
            credits -= turretCost;
            _occupiedCells.Add(cell);

            Vector3 worldPos = new Vector3(cell.x * cellSize, 0f, cell.y * cellSize);
            GameObject turretRoot = new GameObject($"GridAutoTurret_{cell.x}_{cell.y}");
            turretRoot.transform.position = worldPos;

            GameObject baseBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseBlock.transform.SetParent(turretRoot.transform, false);
            baseBlock.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            baseBlock.transform.localScale = new Vector3(1.2f, 1.0f, 1.2f);
            IronFaunaMaterials.Apply(baseBlock, IronFaunaPalette.MaroonRear, false);

            GameObject headBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            headBlock.transform.SetParent(turretRoot.transform, false);
            headBlock.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            headBlock.transform.localScale = new Vector3(0.8f, 0.5f, 1.4f);
            IronFaunaMaterials.Apply(headBlock, IronFaunaPalette.CrimsonFront, false);

            var turret = turretRoot.AddComponent<AutoTurret>();
            turret.turretHead = headBlock.transform;

            var obstacle = turretRoot.AddComponent<NavMeshObstacle>();
            obstacle.carving = true;
            return true;
        }
    }
}
