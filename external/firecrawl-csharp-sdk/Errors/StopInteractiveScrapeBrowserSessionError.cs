using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class StopInteractiveScrapeBrowserSessionError : ApiError
{
    private readonly Optional<ScrapeInteract403Error1> _scrapeInteract403Error1Value;

    private readonly Optional<ScrapeInteract404Error1> _scrapeInteract404Error1Value;

    private StopInteractiveScrapeBrowserSessionError(Optional<ScrapeInteract403Error1> scrapeInteract403Error1Value,
        Optional<ScrapeInteract404Error1> scrapeInteract404Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _scrapeInteract403Error1Value = scrapeInteract403Error1Value;
        _scrapeInteract404Error1Value = scrapeInteract404Error1Value;
    }

    private static StopInteractiveScrapeBrowserSessionError AsScrapeInteract403Error1(ScrapeInteract403Error1 value) =>
        new(Optional<ScrapeInteract403Error1>.Some(value), default, default);

    private static StopInteractiveScrapeBrowserSessionError AsScrapeInteract404Error1(ScrapeInteract404Error1 value) =>
        new(default, Optional<ScrapeInteract404Error1>.Some(value), default);

    private static StopInteractiveScrapeBrowserSessionError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetScrapeInteract403Error1(out ScrapeInteract403Error1 value) =>
        _scrapeInteract403Error1Value.TryGetValue(out value);

    public bool TryGetScrapeInteract404Error1(out ScrapeInteract404Error1 value) =>
        _scrapeInteract404Error1Value.TryGetValue(out value);

    internal static Task<StopInteractiveScrapeBrowserSessionError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            403 => FromJson<ScrapeInteract403Error1>(response, ct).As(AsScrapeInteract403Error1),
            404 => FromJson<ScrapeInteract404Error1>(response, ct).As(AsScrapeInteract404Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class StopInteractiveScrapeBrowserSessionErrorResponse : IErrorResponse<StopInteractiveScrapeBrowserSessionError>
{
    public static StopInteractiveScrapeBrowserSessionErrorResponse Instance { get; } = new();

    private StopInteractiveScrapeBrowserSessionErrorResponse()
    {
    }

    public Task<StopInteractiveScrapeBrowserSessionError> Map(HttpResponseMessage response, CancellationToken ct) =>
        StopInteractiveScrapeBrowserSessionError.Create(response, ct);
}
