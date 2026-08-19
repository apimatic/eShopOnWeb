using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class CancelBatchScrapeError : ApiError
{
    private readonly Optional<BatchScrape404Error1> _batchScrape404Error1Value;

    private readonly Optional<BatchScrape500Error1> _batchScrape500Error1Value;

    private CancelBatchScrapeError(Optional<BatchScrape404Error1> batchScrape404Error1Value,
        Optional<BatchScrape500Error1> batchScrape500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _batchScrape404Error1Value = batchScrape404Error1Value;
        _batchScrape500Error1Value = batchScrape500Error1Value;
    }

    private static CancelBatchScrapeError AsBatchScrape404Error1(BatchScrape404Error1 value) =>
        new(Optional<BatchScrape404Error1>.Some(value), default, default);

    private static CancelBatchScrapeError AsBatchScrape500Error1(BatchScrape500Error1 value) =>
        new(default, Optional<BatchScrape500Error1>.Some(value), default);

    private static CancelBatchScrapeError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetBatchScrape404Error1(out BatchScrape404Error1 value) =>
        _batchScrape404Error1Value.TryGetValue(out value);

    public bool TryGetBatchScrape500Error1(out BatchScrape500Error1 value) =>
        _batchScrape500Error1Value.TryGetValue(out value);

    internal static Task<CancelBatchScrapeError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromJson<BatchScrape404Error1>(response, ct).As(AsBatchScrape404Error1),
            500 => FromJson<BatchScrape500Error1>(response, ct).As(AsBatchScrape500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CancelBatchScrapeErrorResponse : IErrorResponse<CancelBatchScrapeError>
{
    public static CancelBatchScrapeErrorResponse Instance { get; } = new();

    private CancelBatchScrapeErrorResponse()
    {
    }

    public Task<CancelBatchScrapeError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CancelBatchScrapeError.Create(response, ct);
}
