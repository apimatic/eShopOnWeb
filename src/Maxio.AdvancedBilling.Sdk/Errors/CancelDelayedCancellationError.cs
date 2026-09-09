using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class CancelDelayedCancellationError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private CancelDelayedCancellationError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static CancelDelayedCancellationError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static CancelDelayedCancellationError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<CancelDelayedCancellationError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CancelDelayedCancellationErrorResponse : IErrorResponse<CancelDelayedCancellationError>
{
    public static CancelDelayedCancellationErrorResponse Instance { get; } = new();

    private CancelDelayedCancellationErrorResponse()
    {
    }

    public Task<CancelDelayedCancellationError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CancelDelayedCancellationError.Create(response, ct);
}
