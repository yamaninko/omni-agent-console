namespace OmniAgentConsole.Application.Runtime;

/// <summary>Pure checks for shared-lab student quotas.</summary>
public static class SessionQuotaPolicy
{
    public static bool IsOverConcurrent(int runningOrPending, int maxConcurrent)
    {
        if (maxConcurrent <= 0) return false;
        return runningOrPending >= maxConcurrent;
    }

    public static bool IsOverDailyTasks(int createdToday, int maxPerDay)
    {
        if (maxPerDay <= 0) return false;
        return createdToday >= maxPerDay;
    }

    public static bool IsOverDailyTokens(long tokensToday, int maxTokens)
    {
        if (maxTokens <= 0) return false;
        return tokensToday >= maxTokens;
    }
}
