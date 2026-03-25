using UnityEngine;
using TMPro;

/// <summary>
/// Manages the game loop: maze generation, spawning, timer, and win/lose conditions.
/// Each episode: generates a new maze, places player and enemies far apart, runs a 60s timer.
/// Supports multiple enemies all training in the same maze.
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("References")]
    public MazeGenerator mazeGenerator;
    public EnemyAgent[] enemies;
    public PlayerController player;

    [Header("UI (Optional)")]
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI resultText;
    public TextMeshProUGUI statsText;

    [Header("Settings")]
    public float episodeDuration = 30f;

    private float timeRemaining;
    private bool episodeActive;

    // ── Statistics ──
    private int generation;
    private int enemyWins;
    private int playerWins;
    private float fastestCatch = Mathf.Infinity;
    private float totalCatchTime;
    private float lastCatchTime;
    private float totalReward;
    private float lastReward;

    private void Start()
    {
        StartNewEpisode();
    }

    /// <summary>
    /// Sets up a brand new episode with a fresh maze.
    /// </summary>
    public void StartNewEpisode()
    {
        generation++;

        // Generate a new random maze
        mazeGenerator.GenerateNewMaze();

        // Spawn player at a random position
        Vector3 playerPos = mazeGenerator.GetRandomPosition();
        player.transform.position = playerPos;
        player.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // Spawn each enemy at a different position far from the player
        for (int i = 0; i < enemies.Length; i++)
        {
            Vector3 enemyPos = mazeGenerator.GetPositionFarFrom(playerPos);
            enemies[i].transform.position = enemyPos;
            enemies[i].transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }

        // Reset timer
        timeRemaining = episodeDuration;
        episodeActive = true;

        if (resultText != null)
            resultText.text = "";

        UpdateStatsUI();
    }

    private void Update()
    {
        if (!episodeActive) return;

        timeRemaining -= Time.deltaTime;

        // Update timer UI
        if (timerText != null)
            timerText.text = $"Time: {Mathf.CeilToInt(timeRemaining)}s";

        // Update stats live so current reward is visible
        UpdateStatsUI();

        // Time ran out - player survives!
        if (timeRemaining <= 0f)
        {
            OnPlayerSurvived();
        }
    }

    /// <summary>
    /// Called when any enemy touches the player. Enemy wins.
    /// </summary>
    public void OnPlayerCaught()
    {
        if (!episodeActive) return;
        episodeActive = false;

        float catchTime = episodeDuration - timeRemaining;
        enemyWins++;
        totalCatchTime += catchTime;
        lastCatchTime = catchTime;
        if (catchTime < fastestCatch)
            fastestCatch = catchTime;

        RecordReward();

        if (resultText != null)
            resultText.text = $"Enemy caught the player! ({catchTime:F1}s)";

        UpdateStatsUI();

        // Reward the catcher and end all episodes
        for (int i = 0; i < enemies.Length; i++)
        {
            enemies[i].OnCaughtPlayer();
        }

        // Start next episode immediately
        StartNewEpisode();
    }

    /// <summary>
    /// Called when the timer runs out. Player wins.
    /// </summary>
    private void OnPlayerSurvived()
    {
        if (!episodeActive) return;
        episodeActive = false;

        playerWins++;
        RecordReward();

        if (resultText != null)
            resultText.text = "Player survived!";

        UpdateStatsUI();

        for (int i = 0; i < enemies.Length; i++)
        {
            enemies[i].OnTimeUp();
        }

        // Start next episode immediately
        StartNewEpisode();
    }

    /// <summary>
    /// Records the average cumulative reward across all enemies.
    /// </summary>
    private void RecordReward()
    {
        float sum = 0f;
        for (int i = 0; i < enemies.Length; i++)
            sum += enemies[i].GetCumulativeReward();
        lastReward = sum / enemies.Length;
        totalReward += lastReward;
    }

    /// <summary>
    /// Updates the stats panel with current training information.
    /// </summary>
    private void UpdateStatsUI()
    {
        if (statsText == null) return;

        int totalEpisodes = enemyWins + playerWins;
        float winRate = totalEpisodes > 0 ? (enemyWins / (float)totalEpisodes) * 100f : 0f;
        float avgCatch = enemyWins > 0 ? totalCatchTime / enemyWins : 0f;
        string fastest = fastestCatch < Mathf.Infinity ? $"{fastestCatch:F1}s" : "--";
        float avgReward = totalEpisodes > 0 ? totalReward / totalEpisodes : 0f;

        float currentReward = 0f;
        for (int i = 0; i < enemies.Length; i++)
            currentReward += enemies[i].GetCumulativeReward();
        currentReward /= enemies.Length;

        statsText.text =
            $"Generation: {generation}  |  Enemies: {enemies.Length}\n" +
            $"Enemy Wins: {enemyWins}  |  Player Wins: {playerWins}\n" +
            $"Win Rate: {winRate:F1}%\n" +
            $"Avg Catch: {avgCatch:F1}s  |  Best: {fastest}\n" +
            $"Last Catch: {(lastCatchTime > 0f ? $"{lastCatchTime:F1}s" : "--")}\n" +
            $"Reward: {currentReward:F3}  |  Last: {lastReward:F3}  |  Avg: {avgReward:F3}";
    }
}
