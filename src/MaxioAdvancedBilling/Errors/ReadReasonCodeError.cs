using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ReadReasonCodeError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private ReadReasonCodeError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static ReadReasonCodeError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static ReadReasonCodeError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<ReadReasonCodeError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ReadReasonCodeErrorResponse : IErrorResponse<ReadReasonCodeError>
{
    public static ReadReasonCodeErrorResponse Instance { get; } = new();

    private ReadReasonCodeErrorResponse()
    {
    }

    public Task<ReadReasonCodeError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ReadReasonCodeError.Create(response, ct);
}
