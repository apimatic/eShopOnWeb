using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class CancelCrawlError : ApiError
{
    private readonly Optional<Crawl404Error1> _crawl404Error1Value;

    private readonly Optional<Crawl500Error1> _crawl500Error1Value;

    private CancelCrawlError(Optional<Crawl404Error1> crawl404Error1Value,
        Optional<Crawl500Error1> crawl500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _crawl404Error1Value = crawl404Error1Value;
        _crawl500Error1Value = crawl500Error1Value;
    }

    private static CancelCrawlError AsCrawl404Error1(Crawl404Error1 value) =>
        new(Optional<Crawl404Error1>.Some(value), default, default);

    private static CancelCrawlError AsCrawl500Error1(Crawl500Error1 value) =>
        new(default, Optional<Crawl500Error1>.Some(value), default);

    private static CancelCrawlError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetCrawl404Error1(out Crawl404Error1 value) => _crawl404Error1Value.TryGetValue(out value);

    public bool TryGetCrawl500Error1(out Crawl500Error1 value) => _crawl500Error1Value.TryGetValue(out value);

    internal static Task<CancelCrawlError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromJson<Crawl404Error1>(response, ct).As(AsCrawl404Error1),
            500 => FromJson<Crawl500Error1>(response, ct).As(AsCrawl500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CancelCrawlErrorResponse : IErrorResponse<CancelCrawlError>
{
    public static CancelCrawlErrorResponse Instance { get; } = new();

    private CancelCrawlErrorResponse()
    {
    }

    public Task<CancelCrawlError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CancelCrawlError.Create(response, ct);
}
