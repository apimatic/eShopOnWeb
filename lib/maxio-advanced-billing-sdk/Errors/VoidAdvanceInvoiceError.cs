using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class VoidAdvanceInvoiceError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private VoidAdvanceInvoiceError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static VoidAdvanceInvoiceError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static VoidAdvanceInvoiceError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<VoidAdvanceInvoiceError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class VoidAdvanceInvoiceErrorResponse : IErrorResponse<VoidAdvanceInvoiceError>
{
    public static VoidAdvanceInvoiceErrorResponse Instance { get; } = new();

    private VoidAdvanceInvoiceErrorResponse()
    {
    }

    public Task<VoidAdvanceInvoiceError> Map(HttpResponseMessage response, CancellationToken ct) =>
        VoidAdvanceInvoiceError.Create(response, ct);
}
