using UnityEngine;
using UnityEditor;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;

/// <summary>
/// Editor tool that builds the entire MazerunnerAI scene in one click.
/// Access via the Unity menu: MazerunnerAI > Setup Scene.
/// </summary>
public class SceneSetup : EditorWindow
{
    [MenuItem("MazerunnerAI/Setup Scene")]
    public static void SetupScene()
    {
        if (!EditorUtility.DisplayDialog(
            "Setup MazerunnerAI Scene",
            "This will clear the current scene and create the full MazerunnerAI setup.\n\nContinue?",
            "Yes, set it up", "Cancel"))
            return;

        ClearScene();
        CreateFolders();

        // Create prefabs
        GameObject wallPrefab = CreateWallPrefab();
        GameObject floorPrefab = CreateFloorPrefab();

        // Create scene objects
        GameObject gameManagerObj = CreateGameManager(wallPrefab, floorPrefab);
        GameObject playerObj = CreatePlayer();
        GameObject enemyObj = CreateEnemy();
        SetupCamera();
        CreateLight();
        GameObject canvas = CreateUI();

        // Wire all references
        WireReferences(gameManagerObj, playerObj, enemyObj, canvas);

        // Mark scene dirty so the user can save
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        EditorUtility.DisplayDialog("Done!", "Scene setup complete.\n\n" +
            "- Press Play to test with heuristic controls\n" +
            "  (WASD = enemy, Arrow keys = player)\n\n" +
            "- Run train.bat to start ML training\n" +
            "  then press Play in Unity when prompted",
            "OK");
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
    //  Game Manager
    // ─────────────────────────────────────────────

    private static GameObject CreateGameManager(GameObject wallPrefab, GameObject floorPrefab)
    {
        GameObject obj = new GameObject("GameManager");
        Undo.RegisterCreatedObjectUndo(obj, "Create GameManager");

        // Add GameManager script
        obj.AddComponent<GameManager>();

        // Add MazeGenerator and assign prefabs
        MazeGenerator maze = obj.AddComponent<MazeGenerator>();
        maze.wallPrefab = wallPrefab;
        maze.floorPrefab = floorPrefab;

        return obj;
    }

    // ─────────────────────────────────────────────
    //  Player
    // ─────────────────────────────────────────────

    private static GameObject CreatePlayer()
    {
        GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "Player";
        player.tag = "Player";
        player.transform.position = new Vector3(1.5f, 0.5f, 1.5f);
        Undo.RegisterCreatedObjectUndo(player, "Create Player");

        // Rigidbody
        Rigidbody rb = player.AddComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // Player controller
        player.AddComponent<PlayerController>();

        // Material
        player.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("PlayerMaterial", new Color(0.2f, 0.5f, 1f));

        return player;
    }

    // ─────────────────────────────────────────────
    //  Enemy (ML-Agent)
    // ─────────────────────────────────────────────

    private static GameObject CreateEnemy()
    {
        GameObject enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        enemy.name = "Enemy";
        enemy.transform.position = new Vector3(19.5f, 0.5f, 19.5f);
        Undo.RegisterCreatedObjectUndo(enemy, "Create Enemy");

        // Rigidbody
        Rigidbody rb = enemy.AddComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // ML-Agent script
        enemy.AddComponent<EnemyAgent>();

        // Behavior Parameters (added automatically by Agent, but we configure it)
        BehaviorParameters bp = enemy.GetComponent<BehaviorParameters>();
        if (bp == null) bp = enemy.AddComponent<BehaviorParameters>();
        bp.BehaviorName = "MazeChaser";
        bp.BrainParameters.VectorObservationSize = 12; // 4 base + 8 wall rays
        bp.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(2);
        bp.BehaviorType = BehaviorType.Default;

        // Decision Requester
        DecisionRequester dr = enemy.AddComponent<DecisionRequester>();
        dr.DecisionPeriod = 5;

        // Material
        enemy.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("EnemyMaterial", new Color(0.9f, 0.2f, 0.2f));

        return enemy;
    }

    // ─────────────────────────────────────────────
    //  Camera
    // ─────────────────────────────────────────────

    private static void SetupCamera()
    {
        // Top-down camera overlooking the maze (7x7 maze, cellSize 3 = 21x21 units)
        GameObject camObj = new GameObject("Main Camera");
        camObj.tag = "MainCamera";
        Undo.RegisterCreatedObjectUndo(camObj, "Create Camera");

        Camera cam = camObj.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 14f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.15f, 0.15f, 0.2f);

        camObj.AddComponent<AudioListener>();

        // Position above center of a 7x7 maze (21x21 world units)
        camObj.transform.position = new Vector3(10.5f, 30f, 10.5f);
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
        canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Timer text (top center)
        GameObject timerObj = CreateTextElement(canvasObj.transform, "TimerText",
            "Time: 60s", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -20), 28);

        // Result text (center)
        GameObject resultObj = CreateTextElement(canvasObj.transform, "ResultText",
            "", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 40), 36);

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

    // ─────────────────────────────────────────────
    //  Wire References
    // ─────────────────────────────────────────────

    private static void WireReferences(GameObject gameManagerObj, GameObject playerObj,
        GameObject enemyObj, GameObject canvasObj)
    {
        GameManager gm = gameManagerObj.GetComponent<GameManager>();
        MazeGenerator maze = gameManagerObj.GetComponent<MazeGenerator>();
        EnemyAgent enemy = enemyObj.GetComponent<EnemyAgent>();
        PlayerController player = playerObj.GetComponent<PlayerController>();

        // GameManager references
        gm.mazeGenerator = maze;
        gm.enemy = enemy;
        gm.player = player;

        // Enemy references
        enemy.player = playerObj.transform;
        enemy.gameManager = gm;

        // UI references
        Transform timerText = canvasObj.transform.Find("TimerText");
        Transform resultText = canvasObj.transform.Find("ResultText");
        if (timerText != null)
            gm.timerText = timerText.GetComponent<TMPro.TextMeshProUGUI>();
        if (resultText != null)
            gm.resultText = resultText.GetComponent<TMPro.TextMeshProUGUI>();

        // Mark everything as dirty for serialization
        EditorUtility.SetDirty(gm);
        EditorUtility.SetDirty(enemy);
        EditorUtility.SetDirty(maze);
    }
}
