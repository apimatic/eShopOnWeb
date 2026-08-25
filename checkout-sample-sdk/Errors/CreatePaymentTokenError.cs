using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class CreatePaymentTokenError : ApiError
{
    private readonly Optional<Error1> _error1Value;

    private CreatePaymentTokenError(Optional<Error1> error1Value, Optional<RawError> fallback) : base(fallback)
    {
        _error1Value = error1Value;
    }

    private static CreatePaymentTokenError AsError1(Error1 value) =>
        new(Optional<Error1>.Some(value), default);

    private static CreatePaymentTokenError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetError1(out Error1 value) => _error1Value.TryGetValue(out value);

    internal static Task<CreatePaymentTokenError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 403 or 404 or 422 or 500 => FromJson<Error1>(response, ct).As(AsError1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreatePaymentTokenErrorResponse : IErrorResponse<CreatePaymentTokenError>
{
    public static CreatePaymentTokenErrorResponse Instance { get; } = new();

    private CreatePaymentTokenErrorResponse()
    {
    }

    public Task<CreatePaymentTokenError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreatePaymentTokenError.Create(response, ct);
}
