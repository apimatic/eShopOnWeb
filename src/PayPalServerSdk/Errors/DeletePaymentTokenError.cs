using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class DeletePaymentTokenError : ApiError
{
    private readonly Optional<Error> _errorValue;

    private DeletePaymentTokenError(Optional<Error> errorValue, Optional<RawError> fallback) : base(fallback)
    {
        _errorValue = errorValue;
    }

    private static DeletePaymentTokenError AsError(Error value) =>
        new(Optional<Error>.Some(value), default);

    private static DeletePaymentTokenError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    internal static Task<DeletePaymentTokenError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 403 or 500 => FromJson<Error>(response, ct).As(AsError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class DeletePaymentTokenErrorResponse : IErrorResponse<DeletePaymentTokenError>
{
    public static DeletePaymentTokenErrorResponse Instance { get; } = new();

    private DeletePaymentTokenErrorResponse()
    {
    }

    public Task<DeletePaymentTokenError> Map(HttpResponseMessage response, CancellationToken ct) =>
        DeletePaymentTokenError.Create(response, ct);
}
