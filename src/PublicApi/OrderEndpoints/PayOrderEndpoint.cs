using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRouteRequest, IOrderCheckoutService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PayPalSettings _payPalSettings;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor, PayPalSettings payPalSettings)
    {
        _httpContextAccessor = httpContextAccessor;
        _payPalSettings = payPalSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderCheckoutService checkout) =>
            {
                return await HandleAsync(new PayOrderRouteRequest(orderId, request), checkout);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRouteRequest request, IOrderCheckoutService checkout)
    {
        var buyerId = BuyerIdentity.RequireBuyerId(_httpContextAccessor.HttpContext!.User);
        var card = MapCard(request.Body.Card);
        var order = await checkout.PayAsync(buyerId, request.OrderId, request.Body.PaymentMethodId, card);
        return Results.Ok(OrderResponse.From(order, _payPalSettings.Currency));
    }

    internal static CardPaymentSource? MapCard(CardDetailsRequest? card)
    {
        if (card is null)
        {
            return null;
        }

        CardBillingAddress? billing = null;
        if (card.BillingAddress is not null)
        {
            billing = new CardBillingAddress(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.PostalCode,
                string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode) ? "US" : card.BillingAddress.CountryCode);
        }

        return new CardPaymentSource(
            card.Number?.Replace(" ", "") ?? string.Empty,
            card.Expiry,
            card.SecurityCode,
            card.Name,
            billing);
    }
}

public class PayOrderRouteRequest
{
    public PayOrderRouteRequest(int orderId, PayOrderRequest body)
    {
        OrderId = orderId;
        Body = body;
    }

    public int OrderId { get; }
    public PayOrderRequest Body { get; }
}
