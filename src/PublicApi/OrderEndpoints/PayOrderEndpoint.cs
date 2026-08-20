using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, [FromBody] PayOrderRequest request, IOrderPaymentService payments, ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.OrderId = orderId;
                return await HandleAsync(request, payments, buyerId);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService payments) =>
        HandleAsync(request, payments, string.Empty);

    private async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService payments, string buyerId)
    {
        var card = request.Card == null ? null : new PayPalCardDetails(
            request.Card.Number,
            request.Card.Expiry,
            request.Card.SecurityCode,
            request.Card.Name,
            request.Card.BillingAddress == null ? null : new PayPalBillingAddress(
                request.Card.BillingAddress.CountryCode,
                request.Card.BillingAddress.AddressLine1,
                request.Card.BillingAddress.AddressLine2,
                request.Card.BillingAddress.AdminArea1,
                request.Card.BillingAddress.AdminArea2,
                request.Card.BillingAddress.PostalCode));

        var order = await payments.AuthorizePaymentAsync(request.OrderId, buyerId, card, request.PaymentMethodId);
        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderDto.From(order)
        };
        return Results.Ok(response);
    }
}
