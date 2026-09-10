using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ReadSubscriptionsExportError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private ReadSubscriptionsExportError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static ReadSubscriptionsExportError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static ReadSubscriptionsExportError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<ReadSubscriptionsExportError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ReadSubscriptionsExportErrorResponse : IErrorResponse<ReadSubscriptionsExportError>
{
    public static ReadSubscriptionsExportErrorResponse Instance { get; } = new();

    private ReadSubscriptionsExportErrorResponse()
    {
    }

    public Task<ReadSubscriptionsExportError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ReadSubscriptionsExportError.Create(response, ct);
}
