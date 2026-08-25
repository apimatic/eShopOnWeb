using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class CreateSetupTokenError : ApiError
{
    private readonly Optional<Error1> _error1Value;

    private CreateSetupTokenError(Optional<Error1> error1Value, Optional<RawError> fallback) : base(fallback)
    {
        _error1Value = error1Value;
    }

    private static CreateSetupTokenError AsError1(Error1 value) =>
        new(Optional<Error1>.Some(value), default);

    private static CreateSetupTokenError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetError1(out Error1 value) => _error1Value.TryGetValue(out value);

    internal static Task<CreateSetupTokenError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 403 or 422 or 500 => FromJson<Error1>(response, ct).As(AsError1),
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
