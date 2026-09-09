using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class CreateProductCurrencyPricesError : ApiError
{
    private readonly Optional<ErrorArrayMapResponse1> _errorArrayMapResponse1Value;

    private CreateProductCurrencyPricesError(Optional<ErrorArrayMapResponse1> errorArrayMapResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _errorArrayMapResponse1Value = errorArrayMapResponse1Value;
    }

    private static CreateProductCurrencyPricesError AsErrorArrayMapResponse1(ErrorArrayMapResponse1 value) =>
        new(Optional<ErrorArrayMapResponse1>.Some(value), default);

    private static CreateProductCurrencyPricesError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1 value) =>
        _errorArrayMapResponse1Value.TryGetValue(out value);

    internal static Task<CreateProductCurrencyPricesError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<ErrorArrayMapResponse1>(response, ct).As(AsErrorArrayMapResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreateProductCurrencyPricesErrorResponse : IErrorResponse<CreateProductCurrencyPricesError>
{
    public static CreateProductCurrencyPricesErrorResponse Instance { get; } = new();

    private CreateProductCurrencyPricesErrorResponse()
    {
    }

    public Task<CreateProductCurrencyPricesError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreateProductCurrencyPricesError.Create(response, ct);
}
