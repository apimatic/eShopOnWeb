using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class CreateCustomerError : ApiError
{
    private readonly Optional<CustomerErrorResponse1> _customerErrorResponse1Value;

    private CreateCustomerError(Optional<CustomerErrorResponse1> customerErrorResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _customerErrorResponse1Value = customerErrorResponse1Value;
    }

    private static CreateCustomerError AsCustomerErrorResponse1(CustomerErrorResponse1 value) =>
        new(Optional<CustomerErrorResponse1>.Some(value), default);

    private static CreateCustomerError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetCustomerErrorResponse1(out CustomerErrorResponse1 value) =>
        _customerErrorResponse1Value.TryGetValue(out value);

    internal static Task<CreateCustomerError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<CustomerErrorResponse1>(response, ct).As(AsCustomerErrorResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreateCustomerErrorResponse : IErrorResponse<CreateCustomerError>
{
    public static CreateCustomerErrorResponse Instance { get; } = new();

    private CreateCustomerErrorResponse()
    {
    }

    public Task<CreateCustomerError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreateCustomerError.Create(response, ct);
}
