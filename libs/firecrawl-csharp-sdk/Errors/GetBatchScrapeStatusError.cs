using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class GetBatchScrapeStatusError : ApiError
{
    private readonly Optional<BatchScrape402Error1> _batchScrape402Error1Value;

    private readonly Optional<BatchScrape429Error1> _batchScrape429Error1Value;

    private readonly Optional<BatchScrape500Error1> _batchScrape500Error1Value;

    private GetBatchScrapeStatusError(Optional<BatchScrape402Error1> batchScrape402Error1Value,
        Optional<BatchScrape429Error1> batchScrape429Error1Value,
        Optional<BatchScrape500Error1> batchScrape500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _batchScrape402Error1Value = batchScrape402Error1Value;
        _batchScrape429Error1Value = batchScrape429Error1Value;
        _batchScrape500Error1Value = batchScrape500Error1Value;
    }

    private static GetBatchScrapeStatusError AsBatchScrape402Error1(BatchScrape402Error1 value) =>
        new(Optional<BatchScrape402Error1>.Some(value), default, default, default);

    private static GetBatchScrapeStatusError AsBatchScrape429Error1(BatchScrape429Error1 value) =>
        new(default, Optional<BatchScrape429Error1>.Some(value), default, default);

    private static GetBatchScrapeStatusError AsBatchScrape500Error1(BatchScrape500Error1 value) =>
        new(default, default, Optional<BatchScrape500Error1>.Some(value), default);

    private static GetBatchScrapeStatusError AsFallback(RawError value) =>
        new(default, default, default, Optional<RawError>.Some(value));

    public bool TryGetBatchScrape402Error1(out BatchScrape402Error1 value) =>
        _batchScrape402Error1Value.TryGetValue(out value);

    public bool TryGetBatchScrape429Error1(out BatchScrape429Error1 value) =>
        _batchScrape429Error1Value.TryGetValue(out value);

    public bool TryGetBatchScrape500Error1(out BatchScrape500Error1 value) =>
        _batchScrape500Error1Value.TryGetValue(out value);

    internal static Task<GetBatchScrapeStatusError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            402 => FromJson<BatchScrape402Error1>(response, ct).As(AsBatchScrape402Error1),
            429 => FromJson<BatchScrape429Error1>(response, ct).As(AsBatchScrape429Error1),
            500 => FromJson<BatchScrape500Error1>(response, ct).As(AsBatchScrape500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetBatchScrapeStatusErrorResponse : IErrorResponse<GetBatchScrapeStatusError>
{
    public static GetBatchScrapeStatusErrorResponse Instance { get; } = new();

    private GetBatchScrapeStatusErrorResponse()
    {
    }

    public Task<GetBatchScrapeStatusError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetBatchScrapeStatusError.Create(response, ct);
}
