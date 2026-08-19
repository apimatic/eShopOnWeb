using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class CrawlParamsPreviewError : ApiError
{
    private readonly Optional<CrawlParamsPreview400Error1> _crawlParamsPreview400Error1Value;

    private readonly Optional<CrawlParamsPreview401Error1> _crawlParamsPreview401Error1Value;

    private readonly Optional<CrawlParamsPreview500Error1> _crawlParamsPreview500Error1Value;

    private CrawlParamsPreviewError(Optional<CrawlParamsPreview400Error1> crawlParamsPreview400Error1Value,
        Optional<CrawlParamsPreview401Error1> crawlParamsPreview401Error1Value,
        Optional<CrawlParamsPreview500Error1> crawlParamsPreview500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _crawlParamsPreview400Error1Value = crawlParamsPreview400Error1Value;
        _crawlParamsPreview401Error1Value = crawlParamsPreview401Error1Value;
        _crawlParamsPreview500Error1Value = crawlParamsPreview500Error1Value;
    }

    private static CrawlParamsPreviewError AsCrawlParamsPreview400Error1(CrawlParamsPreview400Error1 value) =>
        new(Optional<CrawlParamsPreview400Error1>.Some(value), default, default, default);

    private static CrawlParamsPreviewError AsCrawlParamsPreview401Error1(CrawlParamsPreview401Error1 value) =>
        new(default, Optional<CrawlParamsPreview401Error1>.Some(value), default, default);

    private static CrawlParamsPreviewError AsCrawlParamsPreview500Error1(CrawlParamsPreview500Error1 value) =>
        new(default, default, Optional<CrawlParamsPreview500Error1>.Some(value), default);

    private static CrawlParamsPreviewError AsFallback(RawError value) =>
        new(default, default, default, Optional<RawError>.Some(value));

    public bool TryGetCrawlParamsPreview400Error1(out CrawlParamsPreview400Error1 value) =>
        _crawlParamsPreview400Error1Value.TryGetValue(out value);

    public bool TryGetCrawlParamsPreview401Error1(out CrawlParamsPreview401Error1 value) =>
        _crawlParamsPreview401Error1Value.TryGetValue(out value);

    public bool TryGetCrawlParamsPreview500Error1(out CrawlParamsPreview500Error1 value) =>
        _crawlParamsPreview500Error1Value.TryGetValue(out value);

    internal static Task<CrawlParamsPreviewError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 => FromJson<CrawlParamsPreview400Error1>(response, ct).As(AsCrawlParamsPreview400Error1),
            401 => FromJson<CrawlParamsPreview401Error1>(response, ct).As(AsCrawlParamsPreview401Error1),
            500 => FromJson<CrawlParamsPreview500Error1>(response, ct).As(AsCrawlParamsPreview500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CrawlParamsPreviewErrorResponse : IErrorResponse<CrawlParamsPreviewError>
{
    public static CrawlParamsPreviewErrorResponse Instance { get; } = new();

    private CrawlParamsPreviewErrorResponse()
    {
    }

    public Task<CrawlParamsPreviewError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CrawlParamsPreviewError.Create(response, ct);
}
