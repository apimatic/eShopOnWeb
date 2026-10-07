namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

/// <summary>
/// Values of <c>stoppedBecause</c>.
/// </summary>
public static class WikiWatchStopReasons
{
    /// <summary>The requested watch time ran out while the stream was live.</summary>
    public const string TimeLimit = "time-limit";

    /// <summary>The stream was open but sent nothing for the idle window (15 s by default).</summary>
    public const string NoData = "no-data";

    /// <summary>Wikimedia's answer was not a live stream, or the stream failed; see the reason.</summary>
    public const string StreamError = "stream-error";
}
