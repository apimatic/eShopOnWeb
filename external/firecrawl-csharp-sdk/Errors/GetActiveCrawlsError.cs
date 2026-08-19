using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class GetActiveCrawlsError : ApiError
{
    private readonly Optional<CrawlActive402Error1> _crawlActive402Error1Value;

    private readonly Optional<CrawlActive429Error1> _crawlActive429Error1Value;

    private readonly Optional<CrawlActive500Error1> _crawlActive500Error1Value;

    private GetActiveCrawlsError(Optional<CrawlActive402Error1> crawlActive402Error1Value,
        Optional<CrawlActive429Error1> crawlActive429Error1Value,
        Optional<CrawlActive500Error1> crawlActive500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _crawlActive402Error1Value = crawlActive402Error1Value;
        _crawlActive429Error1Value = crawlActive429Error1Value;
        _crawlActive500Error1Value = crawlActive500Error1Value;
    }

    private static GetActiveCrawlsError AsCrawlActive402Error1(CrawlActive402Error1 value) =>
        new(Optional<CrawlActive402Error1>.Some(value), default, default, default);

    private static GetActiveCrawlsError AsCrawlActive429Error1(CrawlActive429Error1 value) =>
        new(default, Optional<CrawlActive429Error1>.Some(value), default, default);

    private static GetActiveCrawlsError AsCrawlActive500Error1(CrawlActive500Error1 value) =>
        new(default, default, Optional<CrawlActive500Error1>.Some(value), default);

    private static GetActiveCrawlsError AsFallback(RawError value) =>
        new(default, default, default, Optional<RawError>.Some(value));

    public bool TryGetCrawlActive402Error1(out CrawlActive402Error1 value) =>
        _crawlActive402Error1Value.TryGetValue(out value);

    public bool TryGetCrawlActive429Error1(out CrawlActive429Error1 value) =>
        _crawlActive429Error1Value.TryGetValue(out value);

    public bool TryGetCrawlActive500Error1(out CrawlActive500Error1 value) =>
        _crawlActive500Error1Value.TryGetValue(out value);

    internal static Task<GetActiveCrawlsError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            402 => FromJson<CrawlActive402Error1>(response, ct).As(AsCrawlActive402Error1),
            429 => FromJson<CrawlActive429Error1>(response, ct).As(AsCrawlActive429Error1),
            500 => FromJson<CrawlActive500Error1>(response, ct).As(AsCrawlActive500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetActiveCrawlsErrorResponse : IErrorResponse<GetActiveCrawlsError>
{
    public static GetActiveCrawlsErrorResponse Instance { get; } = new();

    private GetActiveCrawlsErrorResponse()
    {
    }

    public Task<GetActiveCrawlsError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetActiveCrawlsError.Create(response, ct);
}
