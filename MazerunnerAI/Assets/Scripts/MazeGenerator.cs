using UnityEngine;

/// <summary>
/// Generates a random maze using recursive backtracking.
/// Each cell is a floor tile surrounded by walls that get removed to carve paths.
/// </summary>
public class MazeGenerator : MonoBehaviour
{
    [Header("Maze Settings")]
    public int width = 7;
    public int height = 7;
    public float cellSize = 3f;

    [Header("Prefabs")]
    public GameObject wallPrefab;
    public GameObject floorPrefab;

    // Internal grid: true = visited
    private bool[,] visited;
    // Walls between cells: [x, y, direction] where 0=right, 1=top
    private bool[,,] walls;

    private Transform mazeParent;

    /// <summary>
    /// Destroys old maze and generates a fresh random one.
    /// Returns the world-space bounds of the maze floor.
    /// </summary>
    public Bounds GenerateNewMaze()
    {
        ClearMaze();

        mazeParent = new GameObject("Maze").transform;
        mazeParent.SetParent(transform);

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

        // Build the 3D maze
        BuildMaze();

        // Calculate bounds
        float totalWidth = width * cellSize;
        float totalHeight = height * cellSize;
        Vector3 center = new Vector3(totalWidth / 2f, 0f, totalHeight / 2f);
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
    /// </summary>
    public Vector3 CellToWorld(int x, int y)
    {
        return new Vector3(
            x * cellSize + cellSize / 2f,
            0.5f,
            y * cellSize + cellSize / 2f
        );
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
                floor.transform.position = cellCenter;
                floor.transform.localScale = new Vector3(cellSize, 0.1f, cellSize);

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
        wall.transform.position = position;
        wall.transform.localScale = scale;
    }
}
