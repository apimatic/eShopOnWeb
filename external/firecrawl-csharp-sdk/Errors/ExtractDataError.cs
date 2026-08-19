using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class ExtractDataError : ApiError
{
    private readonly Optional<Extract400Error1> _extract400Error1Value;

    private readonly Optional<Extract500Error1> _extract500Error1Value;

    private ExtractDataError(Optional<Extract400Error1> extract400Error1Value,
        Optional<Extract500Error1> extract500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _extract400Error1Value = extract400Error1Value;
        _extract500Error1Value = extract500Error1Value;
    }

    private static ExtractDataError AsExtract400Error1(Extract400Error1 value) =>
        new(Optional<Extract400Error1>.Some(value), default, default);

    private static ExtractDataError AsExtract500Error1(Extract500Error1 value) =>
        new(default, Optional<Extract500Error1>.Some(value), default);

    private static ExtractDataError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetExtract400Error1(out Extract400Error1 value) =>
        _extract400Error1Value.TryGetValue(out value);

    public bool TryGetExtract500Error1(out Extract500Error1 value) =>
        _extract500Error1Value.TryGetValue(out value);

    internal static Task<ExtractDataError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 => FromJson<Extract400Error1>(response, ct).As(AsExtract400Error1),
            500 => FromJson<Extract500Error1>(response, ct).As(AsExtract500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ExtractDataErrorResponse : IErrorResponse<ExtractDataError>
{
    public static ExtractDataErrorResponse Instance { get; } = new();

    private ExtractDataErrorResponse()
    {
    }

    public Task<ExtractDataError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ExtractDataError.Create(response, ct);
}
