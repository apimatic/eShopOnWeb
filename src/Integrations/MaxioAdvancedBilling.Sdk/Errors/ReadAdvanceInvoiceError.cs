using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class ReadAdvanceInvoiceError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private ReadAdvanceInvoiceError(Optional<RawError> noContentValue, Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
    }

    private static ReadAdvanceInvoiceError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default);

    private static ReadAdvanceInvoiceError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    internal static Task<ReadAdvanceInvoiceError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ReadAdvanceInvoiceErrorResponse : IErrorResponse<ReadAdvanceInvoiceError>
{
    public static ReadAdvanceInvoiceErrorResponse Instance { get; } = new();

    private ReadAdvanceInvoiceErrorResponse()
    {
    }

    public Task<ReadAdvanceInvoiceError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ReadAdvanceInvoiceError.Create(response, ct);
}
