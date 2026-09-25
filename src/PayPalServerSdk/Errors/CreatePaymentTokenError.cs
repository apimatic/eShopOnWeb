using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class CreatePaymentTokenError : ApiError
{
    private readonly Optional<Error> _errorValue;

    private CreatePaymentTokenError(Optional<Error> errorValue, Optional<RawError> fallback) : base(fallback)
    {
        _errorValue = errorValue;
    }

    private static CreatePaymentTokenError AsError(Error value) => new(Optional<Error>.Some(value), default);

    private static CreatePaymentTokenError AsFallback(RawError value) => new(default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    private static Task<CreatePaymentTokenError> Create(FailedResponse response) =>
        response.StatusCode switch
        {
            400 or 403 or 404 or 422 or 500 => response.Json<Error>().As(AsError),
            _ => response.RawBody().As(AsFallback)
        };

    internal static ApiErrorResponse<CreatePaymentTokenError> Response { get; } = new(Create);
}
