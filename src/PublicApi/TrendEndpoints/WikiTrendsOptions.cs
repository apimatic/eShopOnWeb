using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

/// <summary>
/// Settings for the Wikimedia "trending edits" watch, bound from the <c>WikiTrends</c> configuration section.
/// Every value has a production default, so the section is optional.
/// </summary>
public class WikiTrendsOptions
{
    public const string CONFIG_NAME = "WikiTrends";

    /// <summary>
    /// Wikimedia asks every client to identify itself with a name and a contact address.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string UserAgent { get; set; } = "eShopOnWeb-trends/1.0 (shop-ops@example.com)";

    /// <summary>
    /// Budget for opening the stream (all attempts and back-off included) before the watch window starts.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:00:15")]
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A stream that sends nothing for this long is reported as <c>no-data</c>.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.050", "00:01:00")]
    public TimeSpan NoDataTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Each watch holds one long-lived connection to Wikimedia; extra concurrent requests get 429.
    /// </summary>
    [Range(1, 20)]
    public int MaxConcurrentWatches { get; set; } = 2;

    /// <summary>
    /// Upper bound on the edits listed in <c>matches</c>; the response says when it was reached.
    /// </summary>
    [Range(1, 1000)]
    public int MaxMatches { get; set; } = 100;
}
