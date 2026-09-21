using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ExportProformaInvoicesError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private readonly Optional<SingleErrorResponse1> _singleErrorResponse1Value;

    private ExportProformaInvoicesError(Optional<RawError> noContentValue,
        Optional<SingleErrorResponse1> singleErrorResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
        _singleErrorResponse1Value = singleErrorResponse1Value;
    }

    private static ExportProformaInvoicesError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default, default);

    private static ExportProformaInvoicesError AsSingleErrorResponse1(SingleErrorResponse1 value) =>
        new(default, Optional<SingleErrorResponse1>.Some(value), default);

    private static ExportProformaInvoicesError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    public bool TryGetSingleErrorResponse1(out SingleErrorResponse1 value) =>
        _singleErrorResponse1Value.TryGetValue(out value);

    internal static Task<ExportProformaInvoicesError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            409 => FromJson<SingleErrorResponse1>(response, ct).As(AsSingleErrorResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ExportProformaInvoicesErrorResponse : IErrorResponse<ExportProformaInvoicesError>
{
    public static ExportProformaInvoicesErrorResponse Instance { get; } = new();

    private ExportProformaInvoicesErrorResponse()
    {
    }

    public Task<ExportProformaInvoicesError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ExportProformaInvoicesError.Create(response, ct);
}
