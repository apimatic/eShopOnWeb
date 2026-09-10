using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class GetPaymentTokenError : ApiError
{
    private readonly Optional<Error> _errorValue;

    private GetPaymentTokenError(Optional<Error> errorValue, Optional<RawError> fallback) : base(fallback)
    {
        _errorValue = errorValue;
    }

    private static GetPaymentTokenError AsError(Error value) => new(Optional<Error>.Some(value), default);

    private static GetPaymentTokenError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    internal static Task<GetPaymentTokenError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            403 or 404 or 422 or 500 => FromJson<Error>(response, ct).As(AsError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetPaymentTokenErrorResponse : IErrorResponse<GetPaymentTokenError>
{
    public static GetPaymentTokenErrorResponse Instance { get; } = new();

    private GetPaymentTokenErrorResponse()
    {
    }

    public Task<GetPaymentTokenError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetPaymentTokenError.Create(response, ct);
}
