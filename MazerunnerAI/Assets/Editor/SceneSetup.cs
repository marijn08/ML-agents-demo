using UnityEngine;
using UnityEditor;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;

/// <summary>
/// Editor tool that builds the entire MazerunnerAI scene in one click.
/// Creates 4 independent arenas, each with its own maze, enemy, and player.
/// Access via the Unity menu: MazerunnerAI > Setup Scene.
/// </summary>
public class SceneSetup : EditorWindow
{
    private static readonly int ArenaCount = 4;
    private static readonly float ArenaSpacing = 30f; // space between arena origins

    [MenuItem("MazerunnerAI/Setup Scene")]
    public static void SetupScene()
    {
        if (!EditorUtility.DisplayDialog(
            "Setup MazerunnerAI Scene",
            $"This will clear the current scene and create {ArenaCount} independent training arenas.\n\nContinue?",
            "Yes, set it up", "Cancel"))
            return;

        ClearScene();
        CreateFolders();

        // Create prefabs
        GameObject wallPrefab = CreateWallPrefab();
        GameObject floorPrefab = CreateFloorPrefab();

        // Create global GameManager (stats only)
        GameObject gameManagerObj = new GameObject("GameManager");
        Undo.RegisterCreatedObjectUndo(gameManagerObj, "Create GameManager");
        GameManager globalStats = gameManagerObj.AddComponent<GameManager>();

        // Create arenas
        ArenaManager[] arenaManagers = new ArenaManager[ArenaCount];
        for (int i = 0; i < ArenaCount; i++)
        {
            arenaManagers[i] = CreateArena(i, wallPrefab, floorPrefab, globalStats);
        }
        globalStats.arenas = arenaManagers;

        SetupCamera();
        CreateLight();
        GameObject canvas = CreateUI();

        // Wire UI to global stats
        Transform statsText = canvas.transform.Find("StatsText");
        if (statsText != null)
            globalStats.statsText = statsText.GetComponent<TMPro.TextMeshProUGUI>();

        EditorUtility.SetDirty(globalStats);

        // Mark scene dirty so the user can save
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        EditorUtility.DisplayDialog("Done!", $"Scene setup complete with {ArenaCount} independent arenas.\n\n" +
            "Each arena has its own maze, enemy, and player.\n" +
            "Episodes run independently per arena.\n\n" +
            "- Press Play to test with heuristic controls\n" +
            "- Run train.bat to start ML training",
            "OK");
    }

    /// <summary>
    /// Creates a single arena with its own maze, enemy, and player.
    /// </summary>
    private static ArenaManager CreateArena(int index, GameObject wallPrefab, GameObject floorPrefab, GameManager globalStats)
    {
        // Arena root — offset each arena so they don't overlap
        // Layout: 2x2 grid
        int col = index % 2;
        int row = index / 2;
        Vector3 arenaOrigin = new Vector3(col * ArenaSpacing, 0f, row * ArenaSpacing);

        GameObject arenaRoot = new GameObject($"Arena_{index + 1}");
        arenaRoot.transform.position = arenaOrigin;
        Undo.RegisterCreatedObjectUndo(arenaRoot, $"Create Arena {index + 1}");

        // MazeGenerator on the arena root
        MazeGenerator maze = arenaRoot.AddComponent<MazeGenerator>();
        maze.wallPrefab = wallPrefab;
        maze.floorPrefab = floorPrefab;

        // Player (child of arena)
        GameObject playerObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        playerObj.name = "Player";
        playerObj.tag = "Player";
        playerObj.transform.SetParent(arenaRoot.transform);
        playerObj.transform.localPosition = new Vector3(1.5f, 0.5f, 1.5f);

        Rigidbody playerRb = playerObj.AddComponent<Rigidbody>();
        playerRb.constraints = RigidbodyConstraints.FreezeRotation;
        playerRb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        playerRb.isKinematic = true;

        PlayerController pc = playerObj.AddComponent<PlayerController>();
        pc.autonomous = false;
        playerObj.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("PlayerMaterial", new Color(0.2f, 0.5f, 1f));

        // Enemy (child of arena)
        GameObject enemyObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        enemyObj.name = "Enemy";
        enemyObj.transform.SetParent(arenaRoot.transform);
        enemyObj.transform.localPosition = new Vector3(16.5f, 0.5f, 16.5f);

        Rigidbody enemyRb = enemyObj.AddComponent<Rigidbody>();
        enemyRb.constraints = RigidbodyConstraints.FreezeRotation;
        enemyRb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        EnemyAgent agent = enemyObj.AddComponent<EnemyAgent>();

        BehaviorParameters bp = enemyObj.GetComponent<BehaviorParameters>();
        if (bp == null) bp = enemyObj.AddComponent<BehaviorParameters>();
        bp.BehaviorName = "MazeChaser";
        bp.BrainParameters.VectorObservationSize = 16;
        bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(4);
        bp.BehaviorType = BehaviorType.Default;

        DecisionRequester dr = enemyObj.AddComponent<DecisionRequester>();
        dr.DecisionPeriod = 5;

        // Very generous max steps — episode ends on catch, but has a safety cap
        agent.MaxStep = 25000; // ~250 seconds at decision period 5

        enemyObj.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("EnemyMaterial", new Color(0.9f, 0.2f, 0.2f));

        // ArenaManager on the arena root
        ArenaManager arena = arenaRoot.AddComponent<ArenaManager>();
        arena.mazeGenerator = maze;
        arena.enemy = agent;
        arena.player = pc;
        arena.globalStats = globalStats;

        // Wire agent references
        agent.player = playerObj.transform;
        agent.arenaManager = arena;

        // Wire player enemy references (for flee mode later)
        pc.enemies = new Transform[] { enemyObj.transform };

        EditorUtility.SetDirty(arena);
        EditorUtility.SetDirty(agent);
        EditorUtility.SetDirty(pc);

        return arena;
    }

    // ─────────────────────────────────────────────
    //  Scene cleanup
    // ─────────────────────────────────────────────

    private static void ClearScene()
    {
        // Destroy all root objects in the scene
        foreach (GameObject obj in UnityEngine.SceneManagement.SceneManager
            .GetActiveScene().GetRootGameObjects())
        {
            Undo.DestroyObjectImmediate(obj);
        }
    }

    private static void CreateFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
    }

    // ─────────────────────────────────────────────
    //  Materials
    // ─────────────────────────────────────────────

    private static Material GetOrCreateMaterial(string name, Color color)
    {
        string path = $"Assets/Materials/{name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        // Find URP Lit shader, fall back to Standard
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        mat = new Material(shader) { color = color, name = name };

        // Set URP surface properties
        if (shader.name.Contains("Universal"))
        {
            mat.SetFloat("_Surface", 0); // Opaque
            mat.SetFloat("_Smoothness", 0.3f);
        }

        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // ─────────────────────────────────────────────
    //  Prefabs
    // ─────────────────────────────────────────────

    private static GameObject CreateWallPrefab()
    {
        string path = "Assets/Prefabs/Wall.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Wall";
        wall.tag = "Untagged";
        wall.isStatic = true;
        wall.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("WallMaterial", new Color(0.35f, 0.35f, 0.4f));

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(wall, path);
        Object.DestroyImmediate(wall);
        return prefab;
    }

    private static GameObject CreateFloorPrefab()
    {
        string path = "Assets/Prefabs/Floor.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.tag = "Untagged";
        floor.isStatic = true;
        floor.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("FloorMaterial", new Color(0.85f, 0.85f, 0.8f));

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(floor, path);
        Object.DestroyImmediate(floor);
        return prefab;
    }

    // ─────────────────────────────────────────────
    //  Camera (covers all 4 arenas in 2x2 grid)
    // ─────────────────────────────────────────────

    private static void SetupCamera()
    {
        // 2x2 grid of arenas, each 18x18 units, spaced 25 units apart
        // Center of all arenas: ((0+25)/2, (0+25)/2) = (12.5, 12.5)
        GameObject camObj = new GameObject("Main Camera");
        camObj.tag = "MainCamera";
        Undo.RegisterCreatedObjectUndo(camObj, "Create Camera");

        Camera cam = camObj.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 38f; // large enough to see all 4 arenas with UI space
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.15f, 0.15f, 0.2f);

        camObj.AddComponent<AudioListener>();

        float centerX = ArenaSpacing / 2f + 13f;
        float centerZ = ArenaSpacing / 2f + 13f;
        camObj.transform.position = new Vector3(centerX, 50f, centerZ);
        camObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    // ─────────────────────────────────────────────
    //  Light
    // ─────────────────────────────────────────────

    private static void CreateLight()
    {
        GameObject lightObj = new GameObject("Directional Light");
        Undo.RegisterCreatedObjectUndo(lightObj, "Create Light");

        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.5f;
        light.shadows = LightShadows.Soft;
        light.color = Color.white;

        lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    // ─────────────────────────────────────────────
    //  UI Canvas
    // ─────────────────────────────────────────────

    private static GameObject CreateUI()
    {
        // Create Canvas
        GameObject canvasObj = new GameObject("Canvas");
        Undo.RegisterCreatedObjectUndo(canvasObj, "Create Canvas");

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        UnityEngine.UI.CanvasScaler scaler = canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Stats panel (top-right)
        GameObject statsObj = CreateTextElement(canvasObj.transform, "StatsText",
            "",
            new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(0, 0), 16);
        RectTransform statsRect = statsObj.GetComponent<RectTransform>();
        statsRect.pivot = new Vector2(1f, 1f);
        statsRect.anchoredPosition = new Vector2(-20, -20);
        statsRect.sizeDelta = new Vector2(350, 250);
        statsObj.GetComponent<TMPro.TextMeshProUGUI>().alignment = TMPro.TextAlignmentOptions.TopRight;

        // EventSystem
        if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(esObj, "Create EventSystem");
            esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esObj.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        return canvasObj;
    }

    private static GameObject CreateTextElement(Transform parent, string name,
        string text, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPos, int fontSize)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(400, 60);

        TMPro.TextMeshProUGUI tmp = obj.AddComponent<TMPro.TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = Color.white;

        return obj;
    }

}
