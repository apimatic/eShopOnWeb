using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class CreateOrderTrackingError : ApiError
{
    private readonly Optional<Error> _errorValue;

    private CreateOrderTrackingError(Optional<Error> errorValue, Optional<RawError> fallback) : base(fallback)
    {
        _errorValue = errorValue;
    }

    private static CreateOrderTrackingError AsError(Error value) =>
        new(Optional<Error>.Some(value), default);

    private static CreateOrderTrackingError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    internal static Task<CreateOrderTrackingError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 403 or 404 or 422 or 500 => FromJson<Error>(response, ct).As(AsError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreateOrderTrackingErrorResponse : IErrorResponse<CreateOrderTrackingError>
{
    public static CreateOrderTrackingErrorResponse Instance { get; } = new();

    private CreateOrderTrackingErrorResponse()
    {
    }

    public Task<CreateOrderTrackingError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreateOrderTrackingError.Create(response, ct);
}
