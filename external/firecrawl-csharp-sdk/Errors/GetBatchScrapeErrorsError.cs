using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class GetBatchScrapeErrorsError : ApiError
{
    private readonly Optional<BatchScrapeErrors402Error1> _batchScrapeErrors402Error1Value;

    private readonly Optional<BatchScrapeErrors429Error1> _batchScrapeErrors429Error1Value;

    private readonly Optional<BatchScrapeErrors500Error1> _batchScrapeErrors500Error1Value;

    private GetBatchScrapeErrorsError(Optional<BatchScrapeErrors402Error1> batchScrapeErrors402Error1Value,
        Optional<BatchScrapeErrors429Error1> batchScrapeErrors429Error1Value,
        Optional<BatchScrapeErrors500Error1> batchScrapeErrors500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _batchScrapeErrors402Error1Value = batchScrapeErrors402Error1Value;
        _batchScrapeErrors429Error1Value = batchScrapeErrors429Error1Value;
        _batchScrapeErrors500Error1Value = batchScrapeErrors500Error1Value;
    }

    private static GetBatchScrapeErrorsError AsBatchScrapeErrors402Error1(BatchScrapeErrors402Error1 value) =>
        new(Optional<BatchScrapeErrors402Error1>.Some(value), default, default, default);

    private static GetBatchScrapeErrorsError AsBatchScrapeErrors429Error1(BatchScrapeErrors429Error1 value) =>
        new(default, Optional<BatchScrapeErrors429Error1>.Some(value), default, default);

    private static GetBatchScrapeErrorsError AsBatchScrapeErrors500Error1(BatchScrapeErrors500Error1 value) =>
        new(default, default, Optional<BatchScrapeErrors500Error1>.Some(value), default);

    private static GetBatchScrapeErrorsError AsFallback(RawError value) =>
        new(default, default, default, Optional<RawError>.Some(value));

    public bool TryGetBatchScrapeErrors402Error1(out BatchScrapeErrors402Error1 value) =>
        _batchScrapeErrors402Error1Value.TryGetValue(out value);

    public bool TryGetBatchScrapeErrors429Error1(out BatchScrapeErrors429Error1 value) =>
        _batchScrapeErrors429Error1Value.TryGetValue(out value);

    public bool TryGetBatchScrapeErrors500Error1(out BatchScrapeErrors500Error1 value) =>
        _batchScrapeErrors500Error1Value.TryGetValue(out value);

    internal static Task<GetBatchScrapeErrorsError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            402 => FromJson<BatchScrapeErrors402Error1>(response, ct).As(AsBatchScrapeErrors402Error1),
            429 => FromJson<BatchScrapeErrors429Error1>(response, ct).As(AsBatchScrapeErrors429Error1),
            500 => FromJson<BatchScrapeErrors500Error1>(response, ct).As(AsBatchScrapeErrors500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetBatchScrapeErrorsErrorResponse : IErrorResponse<GetBatchScrapeErrorsError>
{
    public static GetBatchScrapeErrorsErrorResponse Instance { get; } = new();

    private GetBatchScrapeErrorsErrorResponse()
    {
    }

    public Task<GetBatchScrapeErrorsError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetBatchScrapeErrorsError.Create(response, ct);
}
