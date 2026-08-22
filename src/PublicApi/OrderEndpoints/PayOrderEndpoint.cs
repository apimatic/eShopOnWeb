using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public int? PaymentMethodId { get; set; }
    public CardPaymentRequest? Card { get; set; }
}

public class CardPaymentRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CardBillingAddressRequest? BillingAddress { get; set; }
}

public class CardBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderCheckoutService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderCheckoutService checkout) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, checkout);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderCheckoutService checkout)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.RequireBuyerId();
        var card = request.Card == null
            ? null
            : new CardPaymentSource
            {
                Number = request.Card.Number,
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.SecurityCode,
                Name = request.Card.Name,
                BillingAddress = request.Card.BillingAddress == null
                    ? null
                    : new CardBillingAddress
                    {
                        AddressLine1 = request.Card.BillingAddress.AddressLine1,
                        AddressLine2 = request.Card.BillingAddress.AddressLine2,
                        AdminArea2 = request.Card.BillingAddress.AdminArea2,
                        AdminArea1 = request.Card.BillingAddress.AdminArea1,
                        PostalCode = request.Card.BillingAddress.PostalCode,
                        CountryCode = request.Card.BillingAddress.CountryCode
                    }
            };

        var order = await checkout.PayAsync(request.OrderId, buyerId, card, request.PaymentMethodId);
        return Results.Ok(OrderResponseMapper.Map(order));
    }
}
