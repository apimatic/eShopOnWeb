using System.Net.Http;
using System.Threading;
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

    private static CreateSetupTokenError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    internal static Task<CreateSetupTokenError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 403 or 422 or 500 => FromJson<Error>(response, ct).As(AsError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreateSetupTokenErrorResponse : IErrorResponse<CreateSetupTokenError>
{
    public static CreateSetupTokenErrorResponse Instance { get; } = new();

    private CreateSetupTokenErrorResponse()
    {
    }

    public Task<CreateSetupTokenError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreateSetupTokenError.Create(response, ct);
}
