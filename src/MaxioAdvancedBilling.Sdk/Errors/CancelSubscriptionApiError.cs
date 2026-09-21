using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models.AnyOf;

namespace MaxioAdvancedBilling.Errors;

public sealed class CancelSubscriptionApiError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private readonly Optional<CancelSubscriptionErrorResponse> _cancelSubscriptionErrorResponseValue;

    private CancelSubscriptionApiError(Optional<RawError> noContentValue,
        Optional<CancelSubscriptionErrorResponse> cancelSubscriptionErrorResponseValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
        _cancelSubscriptionErrorResponseValue = cancelSubscriptionErrorResponseValue;
    }

    private static CancelSubscriptionApiError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default, default);

    private static CancelSubscriptionApiError AsCancelSubscriptionErrorResponse(CancelSubscriptionErrorResponse value) =>
        new(default, Optional<CancelSubscriptionErrorResponse>.Some(value), default);

    private static CancelSubscriptionApiError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    public bool TryGetCancelSubscriptionErrorResponse(out CancelSubscriptionErrorResponse value) =>
        _cancelSubscriptionErrorResponseValue.TryGetValue(out value);

    internal static Task<CancelSubscriptionApiError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            422 => FromJson<CancelSubscriptionErrorResponse>(response, ct).As(AsCancelSubscriptionErrorResponse),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CancelSubscriptionApiErrorResponse : IErrorResponse<CancelSubscriptionApiError>
{
    public static CancelSubscriptionApiErrorResponse Instance { get; } = new();

    private CancelSubscriptionApiErrorResponse()
    {
    }

    public Task<CancelSubscriptionApiError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CancelSubscriptionApiError.Create(response, ct);
}
