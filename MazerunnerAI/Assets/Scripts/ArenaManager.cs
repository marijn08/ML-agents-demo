using UnityEngine;

/// <summary>
/// Manages a single training arena: one maze, one enemy, one player.
/// Each arena runs its own episode timer and resets independently.
/// </summary>
public class ArenaManager : MonoBehaviour
{
    [Header("References")]
    public MazeGenerator mazeGenerator;
    public EnemyAgent enemy;
    public PlayerController player;

    [Header("Settings")]
    public float episodeDuration = 60f;

    [HideInInspector] public GameManager globalStats;

    private float timeRemaining;
    private bool episodeActive;

    private void Start()
    {
        StartNewEpisode();
    }

    public void StartNewEpisode()
    {
        // Generate a fresh maze for this arena
        mazeGenerator.GenerateNewMaze();

        // Spawn player at a random position
        Vector3 playerPos = mazeGenerator.GetRandomPosition();
        player.transform.position = playerPos;
        player.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // Spawn enemy — 0% chance close, 100% far
        Vector3 enemyPos;
        if (Random.value < 0f)
            enemyPos = mazeGenerator.GetRandomPosition();
        else
            enemyPos = mazeGenerator.GetPositionFarFrom(playerPos);

        enemy.transform.position = enemyPos;
        enemy.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // Reset timer
        timeRemaining = episodeDuration;
        episodeActive = true;
    }

    private void Update()
    {
        if (!episodeActive) return;

        timeRemaining -= Time.deltaTime;

        if (timeRemaining <= 0f)
        {
            OnPlayerSurvived();
        }
    }

    /// <summary>
    /// Called by EnemyAgent.OnCollisionEnter when it touches the player.
    /// </summary>
    public void OnPlayerCaught()
    {
        if (!episodeActive) return;
        episodeActive = false;

        float catchTime = episodeDuration - timeRemaining;

        // Report to global stats
        if (globalStats != null)
            globalStats.RecordCatch(catchTime, enemy.GetCumulativeReward());

        enemy.OnCaughtPlayer();
        StartNewEpisode();
    }

    /// <summary>
    /// Timer expired — player survives this episode.
    /// </summary>
    private void OnPlayerSurvived()
    {
        if (!episodeActive) return;
        episodeActive = false;

        // Report to global stats
        if (globalStats != null)
            globalStats.RecordTimeout(enemy.GetCumulativeReward());

        enemy.OnTimeUp();
        StartNewEpisode();
    }
}
