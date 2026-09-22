using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ReadInvoicesExportError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private ReadInvoicesExportError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static ReadInvoicesExportError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static ReadInvoicesExportError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<ReadInvoicesExportError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ReadInvoicesExportErrorResponse : IErrorResponse<ReadInvoicesExportError>
{
    public static ReadInvoicesExportErrorResponse Instance { get; } = new();

    private ReadInvoicesExportErrorResponse()
    {
    }

    public Task<ReadInvoicesExportError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ReadInvoicesExportError.Create(response, ct);
}
