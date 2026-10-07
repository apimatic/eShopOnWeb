using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

public class WikiEditsRequest : BaseRequest
{
    public const int MinSeconds = 5;
    public const int MaxSeconds = 60;
    public const int DefaultSeconds = 20;

    public int Seconds { get; init; }

    /// <summary>
    /// Fires when the caller disconnects, so the watch stops with it.
    /// </summary>
    public CancellationToken RequestAborted { get; init; }

    public WikiEditsRequest(int? seconds, CancellationToken requestAborted)
    {
        Seconds = seconds ?? DefaultSeconds;
        RequestAborted = requestAborted;
    }

    public bool IsValid => Seconds >= MinSeconds && Seconds <= MaxSeconds;
}
