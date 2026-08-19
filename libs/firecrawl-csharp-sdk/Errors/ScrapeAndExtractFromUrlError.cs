using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class ScrapeAndExtractFromUrlError : ApiError
{
    private readonly Optional<Scrape402Error1> _scrape402Error1Value;

    private readonly Optional<Scrape429Error1> _scrape429Error1Value;

    private readonly Optional<Scrape500Error1> _scrape500Error1Value;

    private ScrapeAndExtractFromUrlError(Optional<Scrape402Error1> scrape402Error1Value,
        Optional<Scrape429Error1> scrape429Error1Value,
        Optional<Scrape500Error1> scrape500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _scrape402Error1Value = scrape402Error1Value;
        _scrape429Error1Value = scrape429Error1Value;
        _scrape500Error1Value = scrape500Error1Value;
    }

    private static ScrapeAndExtractFromUrlError AsScrape402Error1(Scrape402Error1 value) =>
        new(Optional<Scrape402Error1>.Some(value), default, default, default);

    private static ScrapeAndExtractFromUrlError AsScrape429Error1(Scrape429Error1 value) =>
        new(default, Optional<Scrape429Error1>.Some(value), default, default);

    private static ScrapeAndExtractFromUrlError AsScrape500Error1(Scrape500Error1 value) =>
        new(default, default, Optional<Scrape500Error1>.Some(value), default);

    private static ScrapeAndExtractFromUrlError AsFallback(RawError value) =>
        new(default, default, default, Optional<RawError>.Some(value));

    public bool TryGetScrape402Error1(out Scrape402Error1 value) =>
        _scrape402Error1Value.TryGetValue(out value);

    public bool TryGetScrape429Error1(out Scrape429Error1 value) =>
        _scrape429Error1Value.TryGetValue(out value);

    public bool TryGetScrape500Error1(out Scrape500Error1 value) =>
        _scrape500Error1Value.TryGetValue(out value);

    internal static Task<ScrapeAndExtractFromUrlError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            402 => FromJson<Scrape402Error1>(response, ct).As(AsScrape402Error1),
            429 => FromJson<Scrape429Error1>(response, ct).As(AsScrape429Error1),
            500 => FromJson<Scrape500Error1>(response, ct).As(AsScrape500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ScrapeAndExtractFromUrlErrorResponse : IErrorResponse<ScrapeAndExtractFromUrlError>
{
    public static ScrapeAndExtractFromUrlErrorResponse Instance { get; } = new();

    private ScrapeAndExtractFromUrlErrorResponse()
    {
    }

    public Task<ScrapeAndExtractFromUrlError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ScrapeAndExtractFromUrlError.Create(response, ct);
}
