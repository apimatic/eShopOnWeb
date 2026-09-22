using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;

namespace MaxioAdvancedBilling.Errors;

public sealed class RefundPrepaymentApiError : ApiError
{
    private readonly Optional<RefundPrepaymentBaseErrorsResponse1> _refundPrepaymentBaseErrorsResponse1Value;

    private readonly Optional<string> _stringValue;

    private readonly Optional<RefundPrepaymentErrorResponse> _refundPrepaymentErrorResponseValue;

    private RefundPrepaymentApiError(Optional<RefundPrepaymentBaseErrorsResponse1> refundPrepaymentBaseErrorsResponse1Value,
        Optional<string> stringValue,
        Optional<RefundPrepaymentErrorResponse> refundPrepaymentErrorResponseValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _refundPrepaymentBaseErrorsResponse1Value = refundPrepaymentBaseErrorsResponse1Value;
        _stringValue = stringValue;
        _refundPrepaymentErrorResponseValue = refundPrepaymentErrorResponseValue;
    }

    private static RefundPrepaymentApiError AsRefundPrepaymentBaseErrorsResponse1(RefundPrepaymentBaseErrorsResponse1 value) =>
        new(Optional<RefundPrepaymentBaseErrorsResponse1>.Some(value), default, default, default);

    private static RefundPrepaymentApiError AsString(string value) =>
        new(default, Optional<string>.Some(value), default, default);

    private static RefundPrepaymentApiError AsRefundPrepaymentErrorResponse(RefundPrepaymentErrorResponse value) =>
        new(default, default, Optional<RefundPrepaymentErrorResponse>.Some(value), default);

    private static RefundPrepaymentApiError AsFallback(RawError value) =>
        new(default, default, default, Optional<RawError>.Some(value));

    public bool TryGetRefundPrepaymentBaseErrorsResponse1(out RefundPrepaymentBaseErrorsResponse1 value) =>
        _refundPrepaymentBaseErrorsResponse1Value.TryGetValue(out value);

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public bool TryGetRefundPrepaymentErrorResponse(out RefundPrepaymentErrorResponse value) =>
        _refundPrepaymentErrorResponseValue.TryGetValue(out value);

    internal static Task<RefundPrepaymentApiError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 => FromJson<RefundPrepaymentBaseErrorsResponse1>(response, ct).As(AsRefundPrepaymentBaseErrorsResponse1),
            404 => FromJson<string>(response, ct).As(AsString),
            422 => FromJson<RefundPrepaymentErrorResponse>(response, ct).As(AsRefundPrepaymentErrorResponse),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class RefundPrepaymentApiErrorResponse : IErrorResponse<RefundPrepaymentApiError>
{
    public static RefundPrepaymentApiErrorResponse Instance { get; } = new();

    private RefundPrepaymentApiErrorResponse()
    {
    }

    public Task<RefundPrepaymentApiError> Map(HttpResponseMessage response, CancellationToken ct) =>
        RefundPrepaymentApiError.Create(response, ct);
}
