using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ListExportedInvoicesError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private ListExportedInvoicesError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static ListExportedInvoicesError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static ListExportedInvoicesError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<ListExportedInvoicesError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ListExportedInvoicesErrorResponse : IErrorResponse<ListExportedInvoicesError>
{
    public static ListExportedInvoicesErrorResponse Instance { get; } = new();

    private ListExportedInvoicesErrorResponse()
    {
    }

    public Task<ListExportedInvoicesError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ListExportedInvoicesError.Create(response, ct);
}
