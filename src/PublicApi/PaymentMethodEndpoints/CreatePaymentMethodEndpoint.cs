using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsDto Card { get; set; } = new();
}

public class PaymentMethodResponse : BaseResponse
{
    public PaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public PaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }

    public static PaymentMethodResponse From(SavedCardResult result, Guid correlationId) => new(correlationId)
    {
        PaymentMethodId = result.PaymentMethodId,
        LastDigits = result.LastDigits,
        Brand = result.Brand,
        Expiry = result.Expiry,
        CardholderName = result.CardholderName
    };
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreatePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ISavedPaymentMethodService methods) =>
            {
                return await HandleAsync(request, methods);
            })
            .Produces<PaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService methods)
    {
        var buyerId = BuyerIdentity.Require(_httpContextAccessor);
        var result = await methods.SaveAsync(
            buyerId,
            CardDetailsMapping.ToSource(request.Card),
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);
        return Results.Created($"api/payment-methods/{result.PaymentMethodId}", PaymentMethodResponse.From(result, request.CorrelationId()));
    }
}
