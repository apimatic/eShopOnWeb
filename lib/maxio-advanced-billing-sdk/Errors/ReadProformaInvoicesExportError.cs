using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ReadProformaInvoicesExportError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private ReadProformaInvoicesExportError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static ReadProformaInvoicesExportError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static ReadProformaInvoicesExportError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<ReadProformaInvoicesExportError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ReadProformaInvoicesExportErrorResponse : IErrorResponse<ReadProformaInvoicesExportError>
{
    public static ReadProformaInvoicesExportErrorResponse Instance { get; } = new();

    private ReadProformaInvoicesExportErrorResponse()
    {
    }

    public Task<ReadProformaInvoicesExportError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ReadProformaInvoicesExportError.Create(response, ct);
}
