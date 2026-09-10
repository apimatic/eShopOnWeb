using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ReadPaymentProfileError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private ReadPaymentProfileError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static ReadPaymentProfileError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static ReadPaymentProfileError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<ReadPaymentProfileError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ReadPaymentProfileErrorResponse : IErrorResponse<ReadPaymentProfileError>
{
    public static ReadPaymentProfileErrorResponse Instance { get; } = new();

    private ReadPaymentProfileErrorResponse()
    {
    }

    public Task<ReadPaymentProfileError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ReadPaymentProfileError.Create(response, ct);
}
