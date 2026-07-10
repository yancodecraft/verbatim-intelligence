namespace VerbatimIntelligence.Api.Analyses;

/// <summary>
/// The queue carries analysis ids, nothing else — state lives in the
/// database. Key names are part of the contract with the worker.
/// </summary>
public static class RedisKeys
{
    public const string PendingAnalyses = "analyses:pending";
}