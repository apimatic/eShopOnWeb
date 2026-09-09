using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class UpdateCustomerError : ApiError
{
    private readonly Optional<RawError> _noContentValue;

    private readonly Optional<CustomerErrorResponse1> _customerErrorResponse1Value;

    private UpdateCustomerError(Optional<RawError> noContentValue,
        Optional<CustomerErrorResponse1> customerErrorResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _noContentValue = noContentValue;
        _customerErrorResponse1Value = customerErrorResponse1Value;
    }

    private static UpdateCustomerError AsNoContent(RawError value) =>
        new(Optional<RawError>.Some(value), default, default);

    private static UpdateCustomerError AsCustomerErrorResponse1(CustomerErrorResponse1 value) =>
        new(default, Optional<CustomerErrorResponse1>.Some(value), default);

    private static UpdateCustomerError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetNoContent(out RawError value) => _noContentValue.TryGetValue(out value);

    public bool TryGetCustomerErrorResponse1(out CustomerErrorResponse1 value) =>
        _customerErrorResponse1Value.TryGetValue(out value);

    internal static Task<UpdateCustomerError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromRawBody(response, ct).As(AsNoContent),
            422 => FromJson<CustomerErrorResponse1>(response, ct).As(AsCustomerErrorResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class UpdateCustomerErrorResponse : IErrorResponse<UpdateCustomerError>
{
    public static UpdateCustomerErrorResponse Instance { get; } = new();

    private UpdateCustomerErrorResponse()
    {
    }

    public Task<UpdateCustomerError> Map(HttpResponseMessage response, CancellationToken ct) =>
        UpdateCustomerError.Create(response, ct);
}
