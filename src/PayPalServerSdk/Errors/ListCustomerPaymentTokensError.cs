using System.Net.Http;
using System.Threading;
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

    private static ListCustomerPaymentTokensError AsError(Error value) =>
        new(Optional<Error>.Some(value), default);

    private static ListCustomerPaymentTokensError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    internal static Task<ListCustomerPaymentTokensError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 403 or 500 => FromJson<Error>(response, ct).As(AsError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ListCustomerPaymentTokensErrorResponse : IErrorResponse<ListCustomerPaymentTokensError>
{
    public static ListCustomerPaymentTokensErrorResponse Instance { get; } = new();

    private ListCustomerPaymentTokensErrorResponse()
    {
    }

    public Task<ListCustomerPaymentTokensError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ListCustomerPaymentTokensError.Create(response, ct);
}
