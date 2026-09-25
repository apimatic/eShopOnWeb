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

    private static GetAuthorizedPaymentError AsError(Error value) => new(Optional<Error>.Some(value), default, default);

    private static GetAuthorizedPaymentError AsNoContent(RawError value) =>
        new(default, Optional<RawError>.Some(value), default);

    private static GetAuthorizedPaymentError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    private static Task<GetAuthorizedPaymentError> Create(FailedResponse response) =>
        response.StatusCode switch
        {
            401 or 403 or 404 => response.Json<Error>().As(AsError),
            500 => response.RawBody().As(AsNoContent),
            _ => response.RawBody().As(AsFallback)
        };

    internal static ApiErrorResponse<GetAuthorizedPaymentError> Response { get; } = new(Create);
}
