using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class VoidInvoiceError : ApiError
{
    private readonly Optional<object?> _objectValue;

    private readonly Optional<ErrorListResponse1> _errorListResponse1Value;

    private VoidInvoiceError(Optional<object?> objectValue,
        Optional<ErrorListResponse1> errorListResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _objectValue = objectValue;
        _errorListResponse1Value = errorListResponse1Value;
    }

    private static VoidInvoiceError AsObject(object? value) =>
        new(Optional<object?>.Some(value), default, default);

    private static VoidInvoiceError AsErrorListResponse1(ErrorListResponse1 value) =>
        new(default, Optional<ErrorListResponse1>.Some(value), default);

    private static VoidInvoiceError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetObject(out object? value) => _objectValue.TryGetValue(out value);

    public bool TryGetErrorListResponse1(out ErrorListResponse1 value) =>
        _errorListResponse1Value.TryGetValue(out value);

    internal static Task<VoidInvoiceError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromJson<object?>(response, ct).As(AsObject),
            422 => FromJson<ErrorListResponse1>(response, ct).As(AsErrorListResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class VoidInvoiceErrorResponse : IErrorResponse<VoidInvoiceError>
{
    public static VoidInvoiceErrorResponse Instance { get; } = new();

    private VoidInvoiceErrorResponse()
    {
    }

    public Task<VoidInvoiceError> Map(HttpResponseMessage response, CancellationToken ct) =>
        VoidInvoiceError.Create(response, ct);
}
