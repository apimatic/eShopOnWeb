using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class CaptureOrderError : ApiError
{
    private readonly Optional<Error> _errorValue;

    private CaptureOrderError(Optional<Error> errorValue, Optional<RawError> fallback) : base(fallback)
    {
        _errorValue = errorValue;
    }

    private static CaptureOrderError AsError(Error value) => new(Optional<Error>.Some(value), default);

    private static CaptureOrderError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    internal static Task<CaptureOrderError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 401 or 403 or 404 or 422 or 500 => FromJson<Error>(response, ct).As(AsError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CaptureOrderErrorResponse : IErrorResponse<CaptureOrderError>
{
    public static CaptureOrderErrorResponse Instance { get; } = new();

    private CaptureOrderErrorResponse()
    {
    }

    public Task<CaptureOrderError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CaptureOrderError.Create(response, ct);
}
