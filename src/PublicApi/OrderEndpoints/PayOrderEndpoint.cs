using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, HttpContext httpContext, IOrderCheckoutService checkout) =>
            {
                return await HandleAsync(orderId, request, checkout, httpContext.User);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderCheckoutService checkout) =>
        Task.FromResult(Results.BadRequest());

    private async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, IOrderCheckoutService checkout, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name
            ?? throw new ApplicationCore.Exceptions.PaymentException("The caller identity is missing.", 401);

        var card = request.Card == null ? null : ToCard(request.Card);
        var order = await checkout.PayAsync(buyerId, orderId, card, request.PaymentMethodId);

        return Results.Ok(new PayOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDto.From(order)
        });
    }

    internal static CardPaymentSource ToCard(CardDetailsRequest card)
    {
        CardBillingAddress? billing = null;
        if (card.BillingAddress != null)
        {
            billing = new CardBillingAddress(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode);
        }

        return new CardPaymentSource(card.Number, card.Expiry, card.SecurityCode, card.Name, billing);
    }
}
