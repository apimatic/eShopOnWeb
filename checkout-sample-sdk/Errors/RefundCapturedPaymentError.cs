using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class RefundCapturedPaymentError : ApiError
{
    private readonly Optional<Error> _errorValue;

    private readonly Optional<RawError> _noContentValue;

    private RefundCapturedPaymentError(Optional<Error> errorValue,
        Optional<RawError> noContentValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _errorValue = errorValue;
        _noContentValue = noContentValue;
    }

    private static RefundCapturedPaymentError AsError(Error value) =>
        new(Optional<Error>.Some(value), default, default);

    private static RefundCapturedPaymentError AsNoContent(RawError value) =>
        new(default, Optional<RawError>.Some(value), default);

    private static RefundCapturedPaymentError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<RefundCapturedPaymentError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 401 or 403 or 404 or 409 or 422 => FromJson<Error>(response, ct).As(AsError),
            500 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class RefundCapturedPaymentErrorResponse : IErrorResponse<RefundCapturedPaymentError>
{
    public static RefundCapturedPaymentErrorResponse Instance { get; } = new();

    private RefundCapturedPaymentErrorResponse()
    {
    }

    public Task<RefundCapturedPaymentError> Map(HttpResponseMessage response, CancellationToken ct) =>
        RefundCapturedPaymentError.Create(response, ct);
}
