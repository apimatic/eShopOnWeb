using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record BatchScrapeStatusResponseObj
{
    /// <summary>
    /// The current status of the batch scrape. Can be <c>scraping</c>, <c>completed</c>, or <c>failed</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>
    /// The total number of pages that were attempted to be scraped.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("total")]
    public int? Total { get; init; }

    /// <summary>
    /// The number of pages that have been successfully scraped.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("completed")]
    public int? Completed { get; init; }

    /// <summary>
    /// The number of credits used for the batch scrape.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("creditsUsed")]
    public int? CreditsUsed { get; init; }

    /// <summary>
    /// The date and time when the batch scrape will expire.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// The date and time when the batch scrape was started.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// The date and time when the batch scrape finished. Present only when the batch scrape is in a terminal state (<c>completed</c>, <c>failed</c>, or <c>cancelled</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Batch scrape duration in seconds. For terminal batch scrapes, this is the elapsed time from <c>createdAt</c> to <c>completedAt</c>. For in-progress batch scrapes, it is the elapsed time from <c>createdAt</c> to now.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("duration")]
    public double? Duration { get; init; }

    /// <summary>
    /// The URL to retrieve the next 10MB of data. Returned if the batch scrape is not completed or if the response is larger than 10MB.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("next")]
    public string? Next { get; init; }

    /// <summary>
    /// The data of the batch scrape.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("data")]
    public IReadOnlyList<Data2>? Data { get; init; }
}
