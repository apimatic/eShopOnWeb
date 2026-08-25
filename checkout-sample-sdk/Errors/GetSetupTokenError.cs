using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class GetSetupTokenError : ApiError
{
    private readonly Optional<Error1> _error1Value;

    private GetSetupTokenError(Optional<Error1> error1Value, Optional<RawError> fallback) : base(fallback)
    {
        _error1Value = error1Value;
    }

    private static GetSetupTokenError AsError1(Error1 value) => new(Optional<Error1>.Some(value), default);

    private static GetSetupTokenError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetError1(out Error1 value) => _error1Value.TryGetValue(out value);

    internal static Task<GetSetupTokenError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            403 or 404 or 422 or 500 => FromJson<Error1>(response, ct).As(AsError1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetSetupTokenErrorResponse : IErrorResponse<GetSetupTokenError>
{
    public static GetSetupTokenErrorResponse Instance { get; } = new();

    private GetSetupTokenErrorResponse()
    {
    }

    public Task<GetSetupTokenError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetSetupTokenError.Create(response, ct);
}
