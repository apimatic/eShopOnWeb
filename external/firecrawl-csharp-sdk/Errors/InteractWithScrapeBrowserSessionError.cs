using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class InteractWithScrapeBrowserSessionError : ApiError
{
    private readonly Optional<ScrapeInteract400Error1> _scrapeInteract400Error1Value;

    private readonly Optional<ScrapeInteract402Error1> _scrapeInteract402Error1Value;

    private readonly Optional<ScrapeInteract403Error1> _scrapeInteract403Error1Value;

    private readonly Optional<ScrapeInteract404Error1> _scrapeInteract404Error1Value;

    private readonly Optional<ScrapeInteract409Error1> _scrapeInteract409Error1Value;

    private readonly Optional<ScrapeInteract410Error1> _scrapeInteract410Error1Value;

    private readonly Optional<ScrapeInteract429Error1> _scrapeInteract429Error1Value;

    private readonly Optional<ScrapeInteract502Error1> _scrapeInteract502Error1Value;

    private InteractWithScrapeBrowserSessionError(Optional<ScrapeInteract400Error1> scrapeInteract400Error1Value,
        Optional<ScrapeInteract402Error1> scrapeInteract402Error1Value,
        Optional<ScrapeInteract403Error1> scrapeInteract403Error1Value,
        Optional<ScrapeInteract404Error1> scrapeInteract404Error1Value,
        Optional<ScrapeInteract409Error1> scrapeInteract409Error1Value,
        Optional<ScrapeInteract410Error1> scrapeInteract410Error1Value,
        Optional<ScrapeInteract429Error1> scrapeInteract429Error1Value,
        Optional<ScrapeInteract502Error1> scrapeInteract502Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _scrapeInteract400Error1Value = scrapeInteract400Error1Value;
        _scrapeInteract402Error1Value = scrapeInteract402Error1Value;
        _scrapeInteract403Error1Value = scrapeInteract403Error1Value;
        _scrapeInteract404Error1Value = scrapeInteract404Error1Value;
        _scrapeInteract409Error1Value = scrapeInteract409Error1Value;
        _scrapeInteract410Error1Value = scrapeInteract410Error1Value;
        _scrapeInteract429Error1Value = scrapeInteract429Error1Value;
        _scrapeInteract502Error1Value = scrapeInteract502Error1Value;
    }

    private static InteractWithScrapeBrowserSessionError AsScrapeInteract400Error1(ScrapeInteract400Error1 value) =>
        new(Optional<ScrapeInteract400Error1>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    private static InteractWithScrapeBrowserSessionError AsScrapeInteract402Error1(ScrapeInteract402Error1 value) =>
        new(default,
            Optional<ScrapeInteract402Error1>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default,
            default);

    private static InteractWithScrapeBrowserSessionError AsScrapeInteract403Error1(ScrapeInteract403Error1 value) =>
        new(default,
            default,
            Optional<ScrapeInteract403Error1>.Some(value),
            default,
            default,
            default,
            default,
            default,
            default);

    private static InteractWithScrapeBrowserSessionError AsScrapeInteract404Error1(ScrapeInteract404Error1 value) =>
        new(default,
            default,
            default,
            Optional<ScrapeInteract404Error1>.Some(value),
            default,
            default,
            default,
            default,
            default);

    private static InteractWithScrapeBrowserSessionError AsScrapeInteract409Error1(ScrapeInteract409Error1 value) =>
        new(default,
            default,
            default,
            default,
            Optional<ScrapeInteract409Error1>.Some(value),
            default,
            default,
            default,
            default);

    private static InteractWithScrapeBrowserSessionError AsScrapeInteract410Error1(ScrapeInteract410Error1 value) =>
        new(default,
            default,
            default,
            default,
            default,
            Optional<ScrapeInteract410Error1>.Some(value),
            default,
            default,
            default);

    private static InteractWithScrapeBrowserSessionError AsScrapeInteract429Error1(ScrapeInteract429Error1 value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            Optional<ScrapeInteract429Error1>.Some(value),
            default,
            default);

    private static InteractWithScrapeBrowserSessionError AsScrapeInteract502Error1(ScrapeInteract502Error1 value) =>
        new(default,
            default,
            default,
            default,
            default,
            default,
            default,
            Optional<ScrapeInteract502Error1>.Some(value),
            default);

    private static InteractWithScrapeBrowserSessionError AsFallback(RawError value) =>
        new(default, default, default, default, default, default, default, default, Optional<RawError>.Some(value));

    public bool TryGetScrapeInteract400Error1(out ScrapeInteract400Error1 value) =>
        _scrapeInteract400Error1Value.TryGetValue(out value);

    public bool TryGetScrapeInteract402Error1(out ScrapeInteract402Error1 value) =>
        _scrapeInteract402Error1Value.TryGetValue(out value);

    public bool TryGetScrapeInteract403Error1(out ScrapeInteract403Error1 value) =>
        _scrapeInteract403Error1Value.TryGetValue(out value);

    public bool TryGetScrapeInteract404Error1(out ScrapeInteract404Error1 value) =>
        _scrapeInteract404Error1Value.TryGetValue(out value);

    public bool TryGetScrapeInteract409Error1(out ScrapeInteract409Error1 value) =>
        _scrapeInteract409Error1Value.TryGetValue(out value);

    public bool TryGetScrapeInteract410Error1(out ScrapeInteract410Error1 value) =>
        _scrapeInteract410Error1Value.TryGetValue(out value);

    public bool TryGetScrapeInteract429Error1(out ScrapeInteract429Error1 value) =>
        _scrapeInteract429Error1Value.TryGetValue(out value);

    public bool TryGetScrapeInteract502Error1(out ScrapeInteract502Error1 value) =>
        _scrapeInteract502Error1Value.TryGetValue(out value);

    internal static Task<InteractWithScrapeBrowserSessionError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 => FromJson<ScrapeInteract400Error1>(response, ct).As(AsScrapeInteract400Error1),
            402 => FromJson<ScrapeInteract402Error1>(response, ct).As(AsScrapeInteract402Error1),
            403 => FromJson<ScrapeInteract403Error1>(response, ct).As(AsScrapeInteract403Error1),
            404 => FromJson<ScrapeInteract404Error1>(response, ct).As(AsScrapeInteract404Error1),
            409 => FromJson<ScrapeInteract409Error1>(response, ct).As(AsScrapeInteract409Error1),
            410 => FromJson<ScrapeInteract410Error1>(response, ct).As(AsScrapeInteract410Error1),
            429 => FromJson<ScrapeInteract429Error1>(response, ct).As(AsScrapeInteract429Error1),
            502 => FromJson<ScrapeInteract502Error1>(response, ct).As(AsScrapeInteract502Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class InteractWithScrapeBrowserSessionErrorResponse : IErrorResponse<InteractWithScrapeBrowserSessionError>
{
    public static InteractWithScrapeBrowserSessionErrorResponse Instance { get; } = new();

    private InteractWithScrapeBrowserSessionErrorResponse()
    {
    }

    public Task<InteractWithScrapeBrowserSessionError> Map(HttpResponseMessage response, CancellationToken ct) =>
        InteractWithScrapeBrowserSessionError.Create(response, ct);
}
