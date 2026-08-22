using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderCheckoutService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(orderId, request, service, user);
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderCheckoutService service)
        => Task.FromResult(Results.BadRequest());

    private async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, IOrderCheckoutService service, ClaimsPrincipal user)
    {
        var card = request.Card == null ? null : request.Card.ToCardPaymentSource();
        var order = await service.PayAsync(orderId, user.GetBuyerId(), card, request.PaymentMethodId);
        return Results.Ok(order.ToResponse());
    }
}

internal static class PayOrderCardMapping
{
    public static CardPaymentSource ToCardPaymentSource(this PayOrderCardRequest card)
    {
        CardBillingAddress? address = card.BillingAddress == null
            ? null
            : new CardBillingAddress(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode);

        return new CardPaymentSource(card.Number, card.Expiry, card.SecurityCode, card.Name, address);
    }
}
