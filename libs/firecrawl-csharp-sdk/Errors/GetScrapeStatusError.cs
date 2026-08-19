using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class GetScrapeStatusError : ApiError
{
    private readonly Optional<Scrape402Error21> _scrape402Error21Value;

    private readonly Optional<Scrape429Error21> _scrape429Error21Value;

    private readonly Optional<Scrape500Error21> _scrape500Error21Value;

    private GetScrapeStatusError(Optional<Scrape402Error21> scrape402Error21Value,
        Optional<Scrape429Error21> scrape429Error21Value,
        Optional<Scrape500Error21> scrape500Error21Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _scrape402Error21Value = scrape402Error21Value;
        _scrape429Error21Value = scrape429Error21Value;
        _scrape500Error21Value = scrape500Error21Value;
    }

    private static GetScrapeStatusError AsScrape402Error21(Scrape402Error21 value) =>
        new(Optional<Scrape402Error21>.Some(value), default, default, default);

    private static GetScrapeStatusError AsScrape429Error21(Scrape429Error21 value) =>
        new(default, Optional<Scrape429Error21>.Some(value), default, default);

    private static GetScrapeStatusError AsScrape500Error21(Scrape500Error21 value) =>
        new(default, default, Optional<Scrape500Error21>.Some(value), default);

    private static GetScrapeStatusError AsFallback(RawError value) =>
        new(default, default, default, Optional<RawError>.Some(value));

    public bool TryGetScrape402Error21(out Scrape402Error21 value) =>
        _scrape402Error21Value.TryGetValue(out value);

    public bool TryGetScrape429Error21(out Scrape429Error21 value) =>
        _scrape429Error21Value.TryGetValue(out value);

    public bool TryGetScrape500Error21(out Scrape500Error21 value) =>
        _scrape500Error21Value.TryGetValue(out value);

    internal static Task<GetScrapeStatusError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            402 => FromJson<Scrape402Error21>(response, ct).As(AsScrape402Error21),
            429 => FromJson<Scrape429Error21>(response, ct).As(AsScrape429Error21),
            500 => FromJson<Scrape500Error21>(response, ct).As(AsScrape500Error21),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetScrapeStatusErrorResponse : IErrorResponse<GetScrapeStatusError>
{
    public static GetScrapeStatusErrorResponse Instance { get; } = new();

    private GetScrapeStatusErrorResponse()
    {
    }

    public Task<GetScrapeStatusError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetScrapeStatusError.Create(response, ct);
}
