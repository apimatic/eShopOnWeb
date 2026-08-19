using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class GetCrawlErrorsError : ApiError
{
    private readonly Optional<CrawlErrors402Error1> _crawlErrors402Error1Value;

    private readonly Optional<CrawlErrors429Error1> _crawlErrors429Error1Value;

    private readonly Optional<CrawlErrors500Error1> _crawlErrors500Error1Value;

    private GetCrawlErrorsError(Optional<CrawlErrors402Error1> crawlErrors402Error1Value,
        Optional<CrawlErrors429Error1> crawlErrors429Error1Value,
        Optional<CrawlErrors500Error1> crawlErrors500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _crawlErrors402Error1Value = crawlErrors402Error1Value;
        _crawlErrors429Error1Value = crawlErrors429Error1Value;
        _crawlErrors500Error1Value = crawlErrors500Error1Value;
    }

    private static GetCrawlErrorsError AsCrawlErrors402Error1(CrawlErrors402Error1 value) =>
        new(Optional<CrawlErrors402Error1>.Some(value), default, default, default);

    private static GetCrawlErrorsError AsCrawlErrors429Error1(CrawlErrors429Error1 value) =>
        new(default, Optional<CrawlErrors429Error1>.Some(value), default, default);

    private static GetCrawlErrorsError AsCrawlErrors500Error1(CrawlErrors500Error1 value) =>
        new(default, default, Optional<CrawlErrors500Error1>.Some(value), default);

    private static GetCrawlErrorsError AsFallback(RawError value) =>
        new(default, default, default, Optional<RawError>.Some(value));

    public bool TryGetCrawlErrors402Error1(out CrawlErrors402Error1 value) =>
        _crawlErrors402Error1Value.TryGetValue(out value);

    public bool TryGetCrawlErrors429Error1(out CrawlErrors429Error1 value) =>
        _crawlErrors429Error1Value.TryGetValue(out value);

    public bool TryGetCrawlErrors500Error1(out CrawlErrors500Error1 value) =>
        _crawlErrors500Error1Value.TryGetValue(out value);

    internal static Task<GetCrawlErrorsError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            402 => FromJson<CrawlErrors402Error1>(response, ct).As(AsCrawlErrors402Error1),
            429 => FromJson<CrawlErrors429Error1>(response, ct).As(AsCrawlErrors429Error1),
            500 => FromJson<CrawlErrors500Error1>(response, ct).As(AsCrawlErrors500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetCrawlErrorsErrorResponse : IErrorResponse<GetCrawlErrorsError>
{
    public static GetCrawlErrorsErrorResponse Instance { get; } = new();

    private GetCrawlErrorsErrorResponse()
    {
    }

    public Task<GetCrawlErrorsError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetCrawlErrorsError.Create(response, ct);
}
