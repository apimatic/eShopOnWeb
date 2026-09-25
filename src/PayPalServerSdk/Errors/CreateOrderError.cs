using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class CreateOrderError : ApiError
{
    private readonly Optional<Error> _errorValue;

    private CreateOrderError(Optional<Error> errorValue, Optional<RawError> fallback) : base(fallback)
    {
        _errorValue = errorValue;
    }

    private static CreateOrderError AsError(Error value) => new(Optional<Error>.Some(value), default);

    private static CreateOrderError AsFallback(RawError value) => new(default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    private static Task<CreateOrderError> Create(FailedResponse response) =>
        response.StatusCode switch
        {
            400 or 401 or 422 => response.Json<Error>().As(AsError),
            _ => response.RawBody().As(AsFallback)
        };

    internal static ApiErrorResponse<CreateOrderError> Response { get; } = new(Create);
}
