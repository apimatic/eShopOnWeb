using System;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

/// <summary>
/// Settings for the Wikimedia live-edit watcher, bound from the <c>WikimediaTrends</c> configuration section.
/// Every value has a working default; nothing here is secret.
/// </summary>
public class WikimediaTrendsOptions
{
    public const string SectionName = "WikimediaTrends";

    /// <summary>
    /// User-Agent sent to Wikimedia, which asks every client to name itself and give a contact address.
    /// </summary>
    public string UserAgent { get; set; } = "eShopOnWeb-trends/1.0 (shop-ops@example.com)";

    /// <summary>
    /// Optional override of the EventStreams base URL (the SDK default is the public production host).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// How long the stream may send nothing before the watch stops with <c>no-data</c>.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long one connection attempt may wait for Wikimedia to answer (response headers).
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How many times a failed attempt to open the stream is retried, always inside the watch window.
    /// </summary>
    public int MaxConnectRetries { get; set; } = 2;

    /// <summary>
    /// How many watches may hold a connection to Wikimedia at the same time; further requests get 429.
    /// </summary>
    public int MaxConcurrentWatches { get; set; } = 2;

    /// <summary>
    /// Upper bound on the matches returned by one watch; the response says when it was reached.
    /// </summary>
    public int MaxMatches { get; set; } = 200;
}
