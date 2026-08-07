using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/pay — pays for the shopper's order with PayPal, using either a one-off
/// card or one of the shopper's saved cards. Idempotent: a double-click never double-charges.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                PayOrderRequest request,
                ClaimsPrincipal user,
                IOrderPaymentService orderPaymentService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);

                // Treat a card as supplied only when it actually carries a number, so an empty card
                // object alongside a saved-card id is not mistaken for "both instruments supplied".
                var cardProvided = request.Card is not null && !string.IsNullOrWhiteSpace(request.Card.Number);
                CardDetails? card = cardProvided ? CardMapping.ToCardDetails(request.Card) : null;

                var order = await orderPaymentService.PayAsync(
                    buyerId, orderId, card, request.SavedPaymentMethodId, cancellationToken);

                var response = new PayOrderResponse
                {
                    OrderId = order.Id,
                    PaymentStatus = order.PaymentStatus.ToString(),
                    PayPalOrderId = order.PayPalOrderId,
                    PayPalCaptureId = order.PayPalCaptureId
                };
                return Results.Ok(response);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }
}
