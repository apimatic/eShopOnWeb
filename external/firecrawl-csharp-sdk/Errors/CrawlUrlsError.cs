using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class CrawlUrlsError : ApiError
{
    private readonly Optional<Crawl402Error1> _crawl402Error1Value;

    private readonly Optional<Crawl429Error1> _crawl429Error1Value;

    private readonly Optional<Crawl500Error1> _crawl500Error1Value;

    private CrawlUrlsError(Optional<Crawl402Error1> crawl402Error1Value,
        Optional<Crawl429Error1> crawl429Error1Value,
        Optional<Crawl500Error1> crawl500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _crawl402Error1Value = crawl402Error1Value;
        _crawl429Error1Value = crawl429Error1Value;
        _crawl500Error1Value = crawl500Error1Value;
    }

    private static CrawlUrlsError AsCrawl402Error1(Crawl402Error1 value) =>
        new(Optional<Crawl402Error1>.Some(value), default, default, default);

    private static CrawlUrlsError AsCrawl429Error1(Crawl429Error1 value) =>
        new(default, Optional<Crawl429Error1>.Some(value), default, default);

    private static CrawlUrlsError AsCrawl500Error1(Crawl500Error1 value) =>
        new(default, default, Optional<Crawl500Error1>.Some(value), default);

    private static CrawlUrlsError AsFallback(RawError value) =>
        new(default, default, default, Optional<RawError>.Some(value));

    public bool TryGetCrawl402Error1(out Crawl402Error1 value) => _crawl402Error1Value.TryGetValue(out value);

    public bool TryGetCrawl429Error1(out Crawl429Error1 value) => _crawl429Error1Value.TryGetValue(out value);

    public bool TryGetCrawl500Error1(out Crawl500Error1 value) => _crawl500Error1Value.TryGetValue(out value);

    internal static Task<CrawlUrlsError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            402 => FromJson<Crawl402Error1>(response, ct).As(AsCrawl402Error1),
            429 => FromJson<Crawl429Error1>(response, ct).As(AsCrawl429Error1),
            500 => FromJson<Crawl500Error1>(response, ct).As(AsCrawl500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CrawlUrlsErrorResponse : IErrorResponse<CrawlUrlsError>
{
    public static CrawlUrlsErrorResponse Instance { get; } = new();

    private CrawlUrlsErrorResponse()
    {
    }

    public Task<CrawlUrlsError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CrawlUrlsError.Create(response, ct);
}
