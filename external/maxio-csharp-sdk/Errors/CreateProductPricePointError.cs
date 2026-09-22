using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Models;
using MaxioAdvancedBilling.Models;

namespace MaxioAdvancedBilling.Errors;

public sealed class CreateProductPricePointError : ApiError
{
    private readonly Optional<ProductPricePointErrorResponse1> _productPricePointErrorResponse1Value;

    private CreateProductPricePointError(Optional<ProductPricePointErrorResponse1> productPricePointErrorResponse1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _productPricePointErrorResponse1Value = productPricePointErrorResponse1Value;
    }

    private static CreateProductPricePointError AsProductPricePointErrorResponse1(ProductPricePointErrorResponse1 value) =>
        new(Optional<ProductPricePointErrorResponse1>.Some(value), default);

    private static CreateProductPricePointError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetProductPricePointErrorResponse1(out ProductPricePointErrorResponse1 value) =>
        _productPricePointErrorResponse1Value.TryGetValue(out value);

    internal static Task<CreateProductPricePointError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            422 => FromJson<ProductPricePointErrorResponse1>(response, ct).As(AsProductPricePointErrorResponse1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreateProductPricePointErrorResponse : IErrorResponse<CreateProductPricePointError>
{
    public static CreateProductPricePointErrorResponse Instance { get; } = new();

    private CreateProductPricePointErrorResponse()
    {
    }

    public Task<CreateProductPricePointError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreateProductPricePointError.Create(response, ct);
}
