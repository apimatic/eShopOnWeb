using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ReadProformaInvoiceError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private ReadProformaInvoiceError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static ReadProformaInvoiceError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static ReadProformaInvoiceError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<ReadProformaInvoiceError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ReadProformaInvoiceErrorResponse : IErrorResponse<ReadProformaInvoiceError>
{
    public static ReadProformaInvoiceErrorResponse Instance { get; } = new();

    private ReadProformaInvoiceErrorResponse()
    {
    }

    public Task<ReadProformaInvoiceError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ReadProformaInvoiceError.Create(response, ct);
}
