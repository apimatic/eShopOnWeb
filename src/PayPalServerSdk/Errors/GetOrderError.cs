using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class GetOrderError : ApiError
{
    private readonly Optional<Error> _errorValue;

    private GetOrderError(Optional<Error> errorValue, Optional<RawError> fallback) : base(fallback)
    {
        _errorValue = errorValue;
    }

    private static GetOrderError AsError(Error value) => new(Optional<Error>.Some(value), default);

    private static GetOrderError AsFallback(RawError value) => new(default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    internal static Task<GetOrderError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            401 or 404 => FromJson<Error>(response, ct).As(AsError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetOrderErrorResponse : IErrorResponse<GetOrderError>
{
    public static GetOrderErrorResponse Instance { get; } = new();

    private GetOrderErrorResponse()
    {
    }

    public Task<GetOrderError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetOrderError.Create(response, ct);
}
