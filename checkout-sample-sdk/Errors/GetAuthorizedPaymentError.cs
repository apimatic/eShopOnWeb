using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class GetAuthorizedPaymentError : ApiError
{
    private readonly Optional<Error> _errorValue;

    private readonly Optional<RawError> _noContentValue;

    private GetAuthorizedPaymentError(Optional<Error> errorValue,
        Optional<RawError> noContentValue,
        Optional<RawError> fallback) : base(fallback)
    {
        _errorValue = errorValue;
        _noContentValue = noContentValue;
    }

    private static GetAuthorizedPaymentError AsError(Error value) =>
        new(Optional<Error>.Some(value), default, default);

    private static GetAuthorizedPaymentError AsNoContent(RawError value) =>
        new(default, Optional<RawError>.Some(value), default);

    private static GetAuthorizedPaymentError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<GetAuthorizedPaymentError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            401 or 403 or 404 => FromJson<Error>(response, ct).As(AsError),
            500 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetAuthorizedPaymentErrorResponse : IErrorResponse<GetAuthorizedPaymentError>
{
    public static GetAuthorizedPaymentErrorResponse Instance { get; } = new();

    private GetAuthorizedPaymentErrorResponse()
    {
    }

    public Task<GetAuthorizedPaymentError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetAuthorizedPaymentError.Create(response, ct);
}
