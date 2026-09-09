using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models;

namespace PayPalServerSdk.Errors;

public sealed class ConfirmOrderError : ApiError
{
    private readonly Optional<Error> _errorValue;

    private ConfirmOrderError(Optional<Error> errorValue, Optional<RawError> fallback) : base(fallback)
    {
        _errorValue = errorValue;
    }

    private static ConfirmOrderError AsError(Error value) => new(Optional<Error>.Some(value), default);

    private static ConfirmOrderError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetError(out Error value) => _errorValue.TryGetValue(out value);

    internal static Task<ConfirmOrderError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 403 or 422 or 500 => FromJson<Error>(response, ct).As(AsError),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ConfirmOrderErrorResponse : IErrorResponse<ConfirmOrderError>
{
    public static ConfirmOrderErrorResponse Instance { get; } = new();

    private ConfirmOrderErrorResponse()
    {
    }

    public Task<ConfirmOrderError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ConfirmOrderError.Create(response, ct);
}
