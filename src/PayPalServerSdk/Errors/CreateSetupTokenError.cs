using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class CreateSetupTokenError : ApiError
{
    private readonly Optional<Error> _errorValue;

    private CreateSetupTokenError(Optional<Error> errorValue, Optional<RawError> fallback) : base(fallback)
    {
        _errorValue = errorValue;
    }

    private static CreateSetupTokenError AsError(Error value) => new(Optional<Error>.Some(value), default);

    private static CreateSetupTokenError AsFallback(RawError value) => new(default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    private static Task<CreateSetupTokenError> Create(FailedResponse response) =>
        response.StatusCode switch
        {
            400 or 403 or 422 or 500 => response.Json<Error>().As(AsError),
            _ => response.RawBody().As(AsFallback)
        };

    internal static ApiErrorResponse<CreateSetupTokenError> Response { get; } = new(Create);
}
