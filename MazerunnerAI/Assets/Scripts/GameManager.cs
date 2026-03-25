using UnityEngine;
using TMPro;

/// <summary>
/// Manages the game loop: maze generation, spawning, timer, and win/lose conditions.
/// Each episode: generates a new maze, places player and enemy far apart, runs a 60s timer.
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("References")]
    public MazeGenerator mazeGenerator;
    public EnemyAgent enemy;
    public PlayerController player;

    [Header("UI (Optional)")]
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI resultText;

    [Header("Settings")]
    public float episodeDuration = 60f;

    private float timeRemaining;
    private bool episodeActive;

    private void Start()
    {
        StartNewEpisode();
    }

    /// <summary>
    /// Sets up a brand new episode with a fresh maze.
    /// </summary>
    public void StartNewEpisode()
    {
        // Generate a new random maze
        mazeGenerator.GenerateNewMaze();

        // Spawn player at a random position
        Vector3 playerPos = mazeGenerator.GetRandomPosition();
        player.transform.position = playerPos;
        player.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // Spawn enemy far from the player
        Vector3 enemyPos = mazeGenerator.GetPositionFarFrom(playerPos);
        enemy.transform.position = enemyPos;
        enemy.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // Reset timer
        timeRemaining = episodeDuration;
        episodeActive = true;

        if (resultText != null)
            resultText.text = "";
    }

    private void Update()
    {
        if (!episodeActive) return;

        timeRemaining -= Time.deltaTime;

        // Update timer UI
        if (timerText != null)
            timerText.text = $"Time: {Mathf.CeilToInt(timeRemaining)}s";

        // Time ran out - player survives!
        if (timeRemaining <= 0f)
        {
            OnPlayerSurvived();
        }
    }

    /// <summary>
    /// Called when the enemy touches the player. Enemy wins.
    /// </summary>
    public void OnPlayerCaught()
    {
        if (!episodeActive) return;
        episodeActive = false;

        if (resultText != null)
            resultText.text = "Enemy caught the player!";

        enemy.OnCaughtPlayer();

        // Start next episode after a short delay
        Invoke(nameof(StartNewEpisode), 1f);
    }

    /// <summary>
    /// Called when the timer runs out. Player wins.
    /// </summary>
    private void OnPlayerSurvived()
    {
        if (!episodeActive) return;
        episodeActive = false;

        if (resultText != null)
            resultText.text = "Player survived!";

        enemy.OnTimeUp();

        // Start next episode after a short delay
        Invoke(nameof(StartNewEpisode), 1f);
    }
}
