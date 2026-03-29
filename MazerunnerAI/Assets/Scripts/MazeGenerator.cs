using UnityEngine;

/// <summary>
/// Generates a random maze using recursive backtracking.
/// Each cell is a floor tile surrounded by walls that get removed to carve paths.
/// </summary>
public class MazeGenerator : MonoBehaviour
{
    [Header("Maze Settings")]
    public int width = 13;
    public int height = 13;
    public float cellSize = 2f;
    [Range(0f, 0.8f), Tooltip("Fraction of extra walls to remove after carving (0 = normal maze, 0.8 = very open)")]
    public float openness = 0.1f;

    [Header("Prefabs")]
    public GameObject wallPrefab;
    public GameObject floorPrefab;

    // Internal grid: true = visited
    private bool[,] visited;
    // Walls between cells: [x, y, direction] where 0=right, 1=top
    private bool[,,] walls;

    private Transform mazeParent;
    private System.Collections.Generic.Dictionary<Vector2Int, Renderer> floorRenderers;

    /// <summary>
    /// Destroys old maze and generates a fresh random one.
    /// Returns the world-space bounds of the maze floor.
    /// </summary>
    public Bounds GenerateNewMaze()
    {
        ClearMaze();

        mazeParent = new GameObject("Maze").transform;
        mazeParent.SetParent(transform, false);

        floorRenderers = new System.Collections.Generic.Dictionary<Vector2Int, Renderer>(); // false = localPosition stays at 0,0,0

        visited = new bool[width, height];
        // walls[x, y, 0] = wall on the RIGHT side of cell (x,y)
        // walls[x, y, 1] = wall on the TOP side of cell (x,y)
        walls = new bool[width, height, 2];

        // Start all walls as existing
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                walls[x, y, 0] = true;
                walls[x, y, 1] = true;
            }

        // Carve maze using recursive backtracking
        CarveFrom(0, 0);

        // Remove extra walls to make the maze more open
        RemoveExtraWalls();

        // Build the 3D maze
        BuildMaze();

        // Calculate bounds
        float totalWidth = width * cellSize;
        float totalHeight = height * cellSize;
        Vector3 center = transform.position + new Vector3(totalWidth / 2f, 0f, totalHeight / 2f);
        return new Bounds(center, new Vector3(totalWidth, 1f, totalHeight));
    }

    /// <summary>
    /// Gets a random open position in cell coordinates converted to world space.
    /// </summary>
    public Vector3 GetRandomPosition()
    {
        int x = Random.Range(0, width);
        int y = Random.Range(0, height);
        return CellToWorld(x, y);
    }

    /// <summary>
    /// Gets a world position for a specific cell.
    /// Accounts for the MazeGenerator's transform position.
    /// </summary>
    public Vector3 CellToWorld(int x, int y)
    {
        Vector3 local = new Vector3(
            x * cellSize + cellSize / 2f,
            0.5f,
            y * cellSize + cellSize / 2f
        );
        return transform.position + local;
    }

    /// <summary>
    /// Gets a position guaranteed to be far from a given position.
    /// Tries to find a cell at least half the maze diagonal away.
    /// </summary>
    public Vector3 GetPositionFarFrom(Vector3 fromPos, int maxAttempts = 50)
    {
        float minDist = (width + height) * cellSize * 0.3f;

        for (int i = 0; i < maxAttempts; i++)
        {
            Vector3 candidate = GetRandomPosition();
            if (Vector3.Distance(candidate, fromPos) >= minDist)
                return candidate;
        }

        // Fallback: opposite corner
        int fx = Mathf.RoundToInt((fromPos.x - cellSize / 2f) / cellSize);
        int fy = Mathf.RoundToInt((fromPos.z - cellSize / 2f) / cellSize);
        int ox = (fx < width / 2) ? width - 1 : 0;
        int oy = (fy < height / 2) ? height - 1 : 0;
        return CellToWorld(ox, oy);
    }

    private void ClearMaze()
    {
        if (mazeParent != null)
            Destroy(mazeParent.gameObject);
    }

    /// <summary>
    /// Recursive backtracking maze generation.
    /// </summary>
    private void CarveFrom(int x, int y)
    {
        visited[x, y] = true;

        // Randomize direction order
        int[] directions = { 0, 1, 2, 3 };
        Shuffle(directions);

        foreach (int dir in directions)
        {
            int nx = x + DirX(dir);
            int ny = y + DirY(dir);

            if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                continue;
            if (visited[nx, ny])
                continue;

            // Remove wall between current and neighbor
            RemoveWall(x, y, dir);
            CarveFrom(nx, ny);
        }
    }

    private void RemoveWall(int x, int y, int dir)
    {
        switch (dir)
        {
            case 0: walls[x, y, 0] = false; break;       // Right
            case 1: walls[x, y, 1] = false; break;       // Up
            case 2: walls[x - 1, y, 0] = false; break;   // Left (= right wall of neighbor)
            case 3: walls[x, y - 1, 1] = false; break;   // Down (= top wall of neighbor)
        }
    }

    /// <summary>
    /// Randomly removes a fraction of remaining internal walls to create a more open maze.
    /// </summary>
    private void RemoveExtraWalls()
    {
        // Collect all remaining internal walls
        var remaining = new System.Collections.Generic.List<(int x, int y, int d)>();
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                // Right wall (internal only, skip boundary)
                if (x < width - 1 && walls[x, y, 0])
                    remaining.Add((x, y, 0));
                // Top wall (internal only, skip boundary)
                if (y < height - 1 && walls[x, y, 1])
                    remaining.Add((x, y, 1));
            }

        // Shuffle and remove a fraction
        for (int i = remaining.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (remaining[i], remaining[j]) = (remaining[j], remaining[i]);
        }

        int toRemove = Mathf.RoundToInt(remaining.Count * openness);
        for (int i = 0; i < toRemove; i++)
        {
            var (x, y, d) = remaining[i];
            walls[x, y, d] = false;
        }
    }

    private int DirX(int dir) => dir switch { 0 => 1, 2 => -1, _ => 0 };
    private int DirY(int dir) => dir switch { 1 => 1, 3 => -1, _ => 0 };

    private void Shuffle(int[] array)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }

    /// <summary>
    /// Instantiates the floor tiles and wall objects.
    /// </summary>
    private void BuildMaze()
    {
        float wallHeight = 2f;
        float wallThickness = 0.2f;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector3 cellCenter = new Vector3(
                    x * cellSize + cellSize / 2f,
                    0f,
                    y * cellSize + cellSize / 2f
                );

                // Floor
                GameObject floor = Instantiate(floorPrefab, mazeParent);
                floor.transform.localPosition = cellCenter;
                floor.transform.localScale = new Vector3(cellSize, 0.1f, cellSize);
                Renderer floorRend = floor.GetComponent<Renderer>();
                floorRend.material = new Material(floorRend.sharedMaterial);
                floorRend.material.color = Color.white;
                floorRenderers[new Vector2Int(x, y)] = floorRend;

                // Right wall
                if (x == width - 1 || walls[x, y, 0])
                {
                    Vector3 pos = cellCenter + new Vector3(cellSize / 2f, wallHeight / 2f, 0f);
                    CreateWall(pos, new Vector3(wallThickness, wallHeight, cellSize));
                }

                // Top wall
                if (y == height - 1 || walls[x, y, 1])
                {
                    Vector3 pos = cellCenter + new Vector3(0f, wallHeight / 2f, cellSize / 2f);
                    CreateWall(pos, new Vector3(cellSize, wallHeight, wallThickness));
                }

                // Left boundary wall
                if (x == 0)
                {
                    Vector3 pos = cellCenter + new Vector3(-cellSize / 2f, wallHeight / 2f, 0f);
                    CreateWall(pos, new Vector3(wallThickness, wallHeight, cellSize));
                }

                // Bottom boundary wall
                if (y == 0)
                {
                    Vector3 pos = cellCenter + new Vector3(0f, wallHeight / 2f, -cellSize / 2f);
                    CreateWall(pos, new Vector3(cellSize, wallHeight, wallThickness));
                }
            }
        }
    }

    private void CreateWall(Vector3 position, Vector3 scale)
    {
        GameObject wall = Instantiate(wallPrefab, mazeParent);
        wall.transform.localPosition = position;
        wall.transform.localScale = scale;
    }

    /// <summary>
    /// Sets the color of a floor tile at the given maze cell coordinate.
    /// </summary>
    private static readonly int ColorID = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock mpb;

    public void SetFloorColor(Vector2Int cell, Color color)
    {
        if (floorRenderers != null && floorRenderers.TryGetValue(cell, out Renderer rend))
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            mpb.SetColor(ColorID, color);
            rend.SetPropertyBlock(mpb);
        }
    }

    /// <summary>
    /// Converts a world position to a maze cell coordinate.
    /// </summary>
    public Vector2Int WorldToCellCoord(Vector3 worldPos)
    {
        Vector3 local = worldPos - transform.position;
        return new Vector2Int(
            Mathf.FloorToInt(local.x / cellSize),
            Mathf.FloorToInt(local.z / cellSize)
        );
    }

    /// <summary>
    /// Returns true if there is a wall between two adjacent cells.
    /// </summary>
    public bool HasWallBetween(Vector2Int a, Vector2Int b)
    {
        int dx = b.x - a.x;
        int dy = b.y - a.y;
        if (dx == 1 && dy == 0) return walls[a.x, a.y, 0];      // b is RIGHT of a
        if (dx == -1 && dy == 0) return walls[b.x, b.y, 0];     // b is LEFT of a
        if (dx == 0 && dy == 1) return walls[a.x, a.y, 1];      // b is ABOVE a
        if (dx == 0 && dy == -1) return walls[b.x, b.y, 1];     // b is BELOW a
        return true; // not adjacent = treat as wall
    }
}
