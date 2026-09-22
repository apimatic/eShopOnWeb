using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models.AnyOf;

namespace MaxioAdvancedBilling.Errors;

public sealed class DeductServiceCreditApiError : ApiError
{
    private readonly Optional<DeductServiceCreditErrorResponse> _deductServiceCreditErrorResponseValue;

    private DeductServiceCreditApiError(Optional<DeductServiceCreditErrorResponse> deductServiceCreditErrorResponseValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _deductServiceCreditErrorResponseValue = deductServiceCreditErrorResponseValue;
    }

    private static DeductServiceCreditApiError AsDeductServiceCreditErrorResponse(DeductServiceCreditErrorResponse value) =>
        new(Optional<DeductServiceCreditErrorResponse>.Some(value), default);

    private static DeductServiceCreditApiError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetDeductServiceCreditErrorResponse(out DeductServiceCreditErrorResponse value) =>
        _deductServiceCreditErrorResponseValue.TryGetValue(out value);

    internal static Task<DeductServiceCreditApiError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<DeductServiceCreditErrorResponse>(response, ct).As(AsDeductServiceCreditErrorResponse),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class DeductServiceCreditApiErrorResponse : IErrorResponse<DeductServiceCreditApiError>
{
    public static DeductServiceCreditApiErrorResponse Instance { get; } = new();

    private DeductServiceCreditApiErrorResponse()
    {
    }

    public Task<DeductServiceCreditApiError> Map(HttpResponseMessage response, CancellationToken ct) =>
        DeductServiceCreditApiError.Create(response, ct);
}
