using System;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record TeamQueueStatusResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    /// <summary>
    /// Number of jobs currently in your queue
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("jobsInQueue")]
    public double? JobsInQueue { get; init; }

    /// <summary>
    /// Number of jobs currently active
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("activeJobsInQueue")]
    public double? ActiveJobsInQueue { get; init; }

    /// <summary>
    /// Number of jobs currently waiting
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("waitingJobsInQueue")]
    public double? WaitingJobsInQueue { get; init; }

    /// <summary>
    /// Maximum number of concurrent active jobs based on your plan
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxConcurrency")]
    public double? MaxConcurrency { get; init; }

    /// <summary>
    /// Timestamp of the most recent successful job
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("mostRecentSuccess")]
    public DateTimeOffset? MostRecentSuccess { get; init; }
}
