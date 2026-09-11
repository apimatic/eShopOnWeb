using System.Net.Http;
using System.Threading;
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

    private static CreateOrderError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    internal static Task<CreateOrderError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 401 or 422 => FromJson<Error>(response, ct).As(AsError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreateOrderErrorResponse : IErrorResponse<CreateOrderError>
{
    public static CreateOrderErrorResponse Instance { get; } = new();

    private CreateOrderErrorResponse()
    {
    }

    public Task<CreateOrderError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreateOrderError.Create(response, ct);
}
