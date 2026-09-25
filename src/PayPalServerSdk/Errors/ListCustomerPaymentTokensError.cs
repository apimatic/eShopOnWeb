using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class ListCustomerPaymentTokensError : ApiError
{
    private readonly Optional<Error> _errorValue;

    private ListCustomerPaymentTokensError(Optional<Error> errorValue, Optional<RawError> fallback) : base(fallback)
    {
        _errorValue = errorValue;
    }

    private static ListCustomerPaymentTokensError AsError(Error value) => new(Optional<Error>.Some(value), default);

    private static ListCustomerPaymentTokensError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    private static Task<ListCustomerPaymentTokensError> Create(FailedResponse response) =>
        response.StatusCode switch
        {
            400 or 403 or 500 => response.Json<Error>().As(AsError),
            _ => response.RawBody().As(AsFallback)
        };

    internal static ApiErrorResponse<ListCustomerPaymentTokensError> Response { get; } = new(Create);
}
