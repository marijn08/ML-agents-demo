using UnityEngine;
using TMPro;

/// <summary>
/// Global stats tracker ross all training arenas.
/// Each ArenaManager reports catches and timeouts here.
/// </summary>ac
public class GameManager : MonoBehaviour
{
    [Header("Arenas")]
    public ArenaManager[] arenas;

    [Header("UI (Optional)")]
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI resultText;
    public TextMeshProUGUI statsText;

    // ── Statistics ──
    private int totalEpisodes;
    private int enemyWins;
    private int playerWins;
    private float fastestCatch = Mathf.Infinity;
    private float totalCatchTime;
    private float lastCatchTime;
    private float totalReward;
    private float lastReward;
    private float bestReward = float.NegativeInfinity;
    private float worstReward = float.PositiveInfinity;

    private void Update()
    {
        UpdateStatsUI();
    }

    /// <summary>
    /// Called by an ArenaManager when its enemy catches the player.
    /// </summary>
    public void RecordCatch(float catchTime, float cumulativeReward)
    {
        totalEpisodes++;
        enemyWins++;
        totalCatchTime += catchTime;
        lastCatchTime = catchTime;
        if (catchTime < fastestCatch)
            fastestCatch = catchTime;

        lastReward = cumulativeReward;
        totalReward += cumulativeReward;
        if (cumulativeReward > bestReward) bestReward = cumulativeReward;
        if (cumulativeReward < worstReward) worstReward = cumulativeReward;
    }

    /// <summary>
    /// Called by an ArenaManager when its timer expires.
    /// </summary>
    public void RecordTimeout(float cumulativeReward)
    {
        totalEpisodes++;
        playerWins++;

        lastReward = cumulativeReward;
        totalReward += cumulativeReward;
        if (cumulativeReward > bestReward) bestReward = cumulativeReward;
        if (cumulativeReward < worstReward) worstReward = cumulativeReward;
    }

    private void UpdateStatsUI()
    {
        if (statsText == null) return;

        float winRate = totalEpisodes > 0 ? (enemyWins / (float)totalEpisodes) * 100f : 0f;
        float avgCatch = enemyWins > 0 ? totalCatchTime / enemyWins : 0f;
        string fastest = fastestCatch < Mathf.Infinity ? $"{fastestCatch:F1}s" : "--";
        float avgReward = totalEpisodes > 0 ? totalReward / totalEpisodes : 0f;

        string best = bestReward > float.NegativeInfinity ? $"{bestReward:F3}" : "--";
        string worst = worstReward < float.PositiveInfinity ? $"{worstReward:F3}" : "--";

        int arenaCount = arenas != null ? arenas.Length : 0;

        statsText.text =
            $"<b>ARENAS</b>  {arenaCount}\n" +
            $"<b>EPISODES</b>  {totalEpisodes}\n" +
            $"\n" +
            $"<b>WINS</b>\n" +
            $"Enemy: {enemyWins}   Player: {playerWins}\n" +
            $"Win Rate: {winRate:F1}%\n" +
            $"\n" +
            $"<b>CATCH TIMES</b>\n" +
            $"Last: {(lastCatchTime > 0f ? $"{lastCatchTime:F1}s" : "--")}" +
            $"   Avg: {(avgCatch > 0f ? $"{avgCatch:F1}s" : "--")}" +
            $"   Best: {fastest}\n" +
            $"\n" +
            $"<b>REWARDS</b>\n" +
            $"Last: {lastReward:F3}   Avg: {avgReward:F3}\n" +
            $"Best: {best}   Worst: {worst}";
    }
}
