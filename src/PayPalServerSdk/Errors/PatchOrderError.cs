using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class PatchOrderError : ApiError
{
    private readonly Optional<Error> _errorValue;

    private PatchOrderError(Optional<Error> errorValue, Optional<RawError> fallback) : base(fallback)
    {
        _errorValue = errorValue;
    }

    private static PatchOrderError AsError(Error value) => new(Optional<Error>.Some(value), default);

    private static PatchOrderError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    internal static Task<PatchOrderError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 401 or 404 or 422 => FromJson<Error>(response, ct).As(AsError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class PatchOrderErrorResponse : IErrorResponse<PatchOrderError>
{
    public static PatchOrderErrorResponse Instance { get; } = new();

    private PatchOrderErrorResponse()
    {
    }

    public Task<PatchOrderError> Map(HttpResponseMessage response, CancellationToken ct) =>
        PatchOrderError.Create(response, ct);
}
