using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ListExportedProformaInvoicesError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private ListExportedProformaInvoicesError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static ListExportedProformaInvoicesError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static ListExportedProformaInvoicesError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<ListExportedProformaInvoicesError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ListExportedProformaInvoicesErrorResponse : IErrorResponse<ListExportedProformaInvoicesError>
{
    public static ListExportedProformaInvoicesErrorResponse Instance { get; } = new();

    private ListExportedProformaInvoicesErrorResponse()
    {
    }

    public Task<ListExportedProformaInvoicesError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ListExportedProformaInvoicesError.Create(response, ct);
}
