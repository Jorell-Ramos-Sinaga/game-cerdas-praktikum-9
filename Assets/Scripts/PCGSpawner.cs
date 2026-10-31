using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class PCGSpawner : MonoBehaviour
{
    [System.Serializable]
    public class SpawnEntry
    {
        public string name;
        public GameObject prefab;

        [Min(0)]
        public int weight = 1;

        public float yOffset;

        [Min(1)]
        public int cost = 1;
    }

    [Header("Seed")]
    [SerializeField]
    private bool useRandomSeed = false;

    [SerializeField]
    private int seed = 12345;

    [SerializeField]
    private int maxSeedHistory = 5;

    [SerializeField]
    private List<int> seedHistory = new List<int>();


    [Header("References")]
    [SerializeField]
    private Transform player;

    [SerializeField]
    private Transform generatedRoot;


    [Header("Spawn Area")]
    [SerializeField]
    private Vector2 spawnAreaSize =
        new Vector2(24f, 24f);

    [SerializeField]
    private float spawnY = 0.5f;


    [Header("Spawn Counts")]
    [SerializeField]
    private int enemyCount = 12;

    [SerializeField]
    private int itemCount = 8;


    [Header("Difficulty Budget")]
    [SerializeField]
    private bool useDifficultyBudget = false;

    [SerializeField]
    private int difficultyBudget = 20;


    [Header("Validation")]
    [SerializeField]
    private float minDistanceFromPlayer = 5f;

    [SerializeField]
    private float checkRadius = 0.75f;

    [SerializeField]
    private float minimumSpacing = 1.5f;

    [SerializeField]
    private LayerMask obstacleMask;

    [SerializeField]
    private bool useNavMeshValidation = false;

    [SerializeField]
    private float navMeshSampleDistance = 1.0f;

    [SerializeField]
    private int maxAttemptsPerObject = 50;


    [Header("Enemy Weighted Table")]
    [SerializeField]
    private SpawnEntry[] enemyEntries;


    [Header("Item Weighted Table")]
    [SerializeField]
    private SpawnEntry[] itemEntries;


    [Header("Runtime Debug")]
    [SerializeField]
    private int currentSeed;

    [SerializeField]
    private int successfulEnemySpawns;

    [SerializeField]
    private int successfulItemSpawns;

    [SerializeField]
    private int currentBudgetSpent;

    [SerializeField]
    private int rejectedPositions;


    [Header("GUI Configuration")]
    [SerializeField]
    private string spawnerName = "";

    [SerializeField]
    private bool autoGuiOffset = true;

    [SerializeField]
    private Vector2 guiPosition = new Vector2(10f, 10f);

    private static readonly List<PCGSpawner> allSpawners =
        new List<PCGSpawner>();

    private readonly List<Vector3> usedPositions =
        new List<Vector3>();

    private readonly Dictionary<string, int> enemyStats =
        new Dictionary<string, int>();

    private readonly Dictionary<string, int> itemStats =
        new Dictionary<string, int>();

    private void OnEnable()
    {
        if (!allSpawners.Contains(this))
        {
            allSpawners.Add(this);
        }
    }

    private void OnDisable()
    {
        allSpawners.Remove(this);
    }


    private void Start()
    {
        Generate();
    }


    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            RegenerateRandom();
        }
    }


    public void RegenerateRandom()
    {
        useRandomSeed = true;
        seed = Random.Range(10000, 999999);
        Generate();
    }


    public void Generate()
    {
        ClearGeneratedObjects();

        usedPositions.Clear();

        successfulEnemySpawns = 0;
        successfulItemSpawns = 0;
        currentBudgetSpent = 0;
        rejectedPositions = 0;

        enemyStats.Clear();
        itemStats.Clear();


        if (useRandomSeed)
        {
            seed = Random.Range(10000, 999999);
        }

        currentSeed = seed;

        if (!seedHistory.Contains(currentSeed))
        {
            seedHistory.Insert(0, currentSeed);
            if (seedHistory.Count > maxSeedHistory)
            {
                seedHistory.RemoveAt(seedHistory.Count - 1);
            }
        }

        Random.InitState(currentSeed);


        SpawnGroup(
            enemyEntries,
            enemyCount,
            true
        );

        SpawnGroup(
            itemEntries,
            itemCount,
            false
        );


        Debug.Log(
            $"PCG Generation Complete | " +
            $"Seed: {currentSeed} | " +
            $"Enemies: {successfulEnemySpawns}/{enemyCount} | " +
            $"Items: {successfulItemSpawns}/{itemCount} | " +
            $"Rejected Positions: {rejectedPositions}"
        );
    }


    public void GenerateWithSeed(int targetSeed)
    {
        useRandomSeed = false;
        seed = targetSeed;
        Generate();
    }


    private void SpawnGroup(
        SpawnEntry[] entries,
        int count,
        bool enemyGroup)
    {
        if (entries == null || entries.Length == 0)
        {
            Debug.LogWarning(
                "Spawn table kosong."
            );

            return;
        }

        bool useBudget = enemyGroup && useDifficultyBudget;
        int remainingBudget = difficultyBudget;
        int targetCount = useBudget ? int.MaxValue : count;
        int spawnedCount = 0;

        while (spawnedCount < targetCount)
        {
            if (useBudget)
            {
                SpawnEntry testFit = SelectWeightedEntry(entries, remainingBudget);
                if (testFit == null)
                {
                    // Tidak ada enemy lagi yang muat di sisa budget
                    break;
                }
            }

            bool spawned = false;

            for (
                int attempt = 0;
                attempt < maxAttemptsPerObject;
                attempt++)
            {
                Vector3 candidate =
                    GenerateRandomPosition();

                if (!IsPositionValid(candidate))
                {
                    rejectedPositions++;
                    continue;
                }

                SpawnEntry selectedEntry =
                    SelectWeightedEntry(
                        entries,
                        useBudget ? remainingBudget : int.MaxValue
                    );

                if (selectedEntry == null || selectedEntry.prefab == null)
                {
                    break;
                }

                Quaternion rotation =
                    Quaternion.Euler(
                        0f,
                        Random.Range(0f, 360f),
                        0f
                    );

                Vector3 spawnPosition =
                    candidate + new Vector3(0f, selectedEntry.yOffset, 0f);

                GameObject spawnedObject =
                    Instantiate(
                        selectedEntry.prefab,
                        spawnPosition,
                        rotation,
                        generatedRoot
                    );

                spawnedObject.name =
                    $"{selectedEntry.prefab.name}_Generated_{spawnedCount}";

                usedPositions.Add(candidate);

                string entryName = string.IsNullOrEmpty(selectedEntry.name)
                    ? selectedEntry.prefab.name
                    : selectedEntry.name;

                if (enemyGroup)
                {
                    successfulEnemySpawns++;
                    if (useBudget)
                    {
                        remainingBudget -= selectedEntry.cost;
                        currentBudgetSpent += selectedEntry.cost;
                    }

                    if (!enemyStats.ContainsKey(entryName))
                    {
                        enemyStats[entryName] = 0;
                    }
                    enemyStats[entryName]++;
                }
                else
                {
                    successfulItemSpawns++;

                    if (!itemStats.ContainsKey(entryName))
                    {
                        itemStats[entryName] = 0;
                    }
                    itemStats[entryName]++;
                }

                spawned = true;
                spawnedCount++;
                break;
            }

            if (!spawned)
            {
                Debug.LogWarning(
                    $"Gagal spawn object ke-{spawnedCount}. " +
                    $"Tidak menemukan posisi valid setelah " +
                    $"{maxAttemptsPerObject} percobaan."
                );
                break;
            }
        }
    }


    private Vector3 GenerateRandomPosition()
    {
        float halfWidth =
            spawnAreaSize.x * 0.5f;

        float halfDepth =
            spawnAreaSize.y * 0.5f;


        float randomX =
            Random.Range(-halfWidth, halfWidth);

        float randomZ =
            Random.Range(-halfDepth, halfDepth);


        return transform.position +
            new Vector3(
                randomX,
                spawnY,
                randomZ
            );
    }


    private bool IsPositionValid(
        Vector3 candidate)
    {
        // Rule 1:
        // Jangan spawn terlalu dekat dengan player.
        if (player != null)
        {
            Vector3 playerFlat =
                new Vector3(
                    player.position.x,
                    candidate.y,
                    player.position.z
                );


            float distanceToPlayer =
                Vector3.Distance(
                    candidate,
                    playerFlat
                );


            if (distanceToPlayer <
                minDistanceFromPlayer)
            {
                return false;
            }
        }


        // Rule 2:
        // Jangan spawn di dalam obstacle.
        bool blocked =
            Physics.CheckSphere(
                candidate,
                checkRadius,
                obstacleMask,
                QueryTriggerInteraction.Ignore
            );


        if (blocked)
        {
            return false;
        }


        // Rule 3:
        // Jangan terlalu dekat dengan
        // procedural object sebelumnya.
        for (
            int i = 0;
            i < usedPositions.Count;
            i++)
        {
            float distance =
                Vector3.Distance(
                    candidate,
                    usedPositions[i]
                );


            if (distance < minimumSpacing)
            {
                return false;
            }
        }


        // Rule 4:
        // Validasi posisi harus berada di walkable NavMesh.
        if (useNavMeshValidation)
        {
            if (!NavMesh.SamplePosition(
                    candidate,
                    out NavMeshHit hit,
                    navMeshSampleDistance,
                    NavMesh.AllAreas))
            {
                return false;
            }
        }


        return true;
    }


    private SpawnEntry SelectWeightedEntry(
        SpawnEntry[] entries,
        int maxCost = int.MaxValue)
    {
        int totalWeight = 0;


        foreach (SpawnEntry entry in entries)
        {
            if (
                entry != null &&
                entry.prefab != null &&
                entry.weight > 0 &&
                entry.cost <= maxCost)
            {
                totalWeight += entry.weight;
            }
        }


        if (totalWeight <= 0)
        {
            return null;
        }


        int randomValue =
            Random.Range(0, totalWeight);


        int cumulativeWeight = 0;


        foreach (SpawnEntry entry in entries)
        {
            if (
                entry == null ||
                entry.prefab == null ||
                entry.weight <= 0 ||
                entry.cost > maxCost)
            {
                continue;
            }


            cumulativeWeight +=
                entry.weight;


            if (
                randomValue <
                cumulativeWeight)
            {
                return entry;
            }
        }


        return null;
    }


    private void ClearGeneratedObjects()
    {
        if (generatedRoot == null)
        {
            return;
        }


        for (
            int i =
                generatedRoot.childCount - 1;
            i >= 0;
            i--)
        {
            GameObject child =
                generatedRoot
                    .GetChild(i)
                    .gameObject;

            child.SetActive(false);

            Destroy(child);
        }
    }


    private void OnGUI()
    {
        int index = allSpawners.IndexOf(this);
        float xPos = (autoGuiOffset && index >= 0) ? (10f + index * 320f) : guiPosition.x;
        float yPos = guiPosition.y;

        string title = string.IsNullOrEmpty(spawnerName) ? gameObject.name : spawnerName;

        int totalStatLines = enemyStats.Count + itemStats.Count;
        float historyHeight = seedHistory.Count > 0 ? 55f : 0f;
        float boxHeight = 150f + (totalStatLines * 20f) + historyHeight;

        GUI.Box(
            new Rect(xPos, yPos, 310, boxHeight),
            $"PCG Debug - {title}"
        );

        float lineY = yPos + 30f;

        GUI.Label(
            new Rect(xPos + 10, lineY, 290, 20),
            $"Seed: {currentSeed}"
        );
        lineY += 20f;

        string enemyText = useDifficultyBudget
            ? $"Enemy: {successfulEnemySpawns} (Budget: {currentBudgetSpent}/{difficultyBudget})"
            : $"Enemy: {successfulEnemySpawns}/{enemyCount}";

        GUI.Label(
            new Rect(xPos + 10, lineY, 290, 20),
            enemyText
        );
        lineY += 20f;

        foreach (var kvp in enemyStats)
        {
            float pct = successfulEnemySpawns > 0 ? (kvp.Value * 100f / successfulEnemySpawns) : 0f;
            GUI.Label(
                new Rect(xPos + 25, lineY, 275, 20),
                $"• {kvp.Key}: {kvp.Value} ({pct:F1}%)"
            );
            lineY += 20f;
        }

        GUI.Label(
            new Rect(xPos + 10, lineY, 290, 20),
            $"Item: {successfulItemSpawns}/{itemCount}"
        );
        lineY += 20f;

        foreach (var kvp in itemStats)
        {
            float pct = successfulItemSpawns > 0 ? (kvp.Value * 100f / successfulItemSpawns) : 0f;
            GUI.Label(
                new Rect(xPos + 25, lineY, 275, 20),
                $"• {kvp.Key}: {kvp.Value} ({pct:F1}%)"
            );
            lineY += 20f;
        }

        GUI.Label(
            new Rect(xPos + 10, lineY, 290, 20),
            $"Rejected: {rejectedPositions}"
        );
        lineY += 20f;

        if (GUI.Button(new Rect(xPos + 10, lineY, 140, 22), "Regenerate (R)"))
        {
            RegenerateRandom();
        }
        lineY += 26f;

        if (seedHistory.Count > 0)
        {
            GUI.Label(
                new Rect(xPos + 10, lineY, 290, 20),
                "Recent Seeds (Click to load):"
            );
            lineY += 22f;

            float btnX = xPos + 10f;
            for (int h = 0; h < seedHistory.Count; h++)
            {
                int s = seedHistory[h];
                string label = (s == currentSeed) ? $"[{s}]" : $"{s}";
                if (GUI.Button(new Rect(btnX, lineY, 56, 20), label))
                {
                    GenerateWithSeed(s);
                }
                btnX += 58f;
                if (btnX + 56f > xPos + 300f)
                {
                    btnX = xPos + 10f;
                    lineY += 22f;
                }
            }
        }
    }


    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;


        Vector3 center =
            transform.position +
            Vector3.up * spawnY;


        Vector3 size =
            new Vector3(
                spawnAreaSize.x,
                0.1f,
                spawnAreaSize.y
            );


        Gizmos.DrawWireCube(
            center,
            size
        );


        if (player != null)
        {
            Gizmos.color = Color.red;

            Gizmos.DrawWireSphere(
                player.position,
                minDistanceFromPlayer
            );
        }
    }
}