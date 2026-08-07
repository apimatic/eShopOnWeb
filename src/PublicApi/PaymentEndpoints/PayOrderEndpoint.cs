using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST api/orders/{orderId}/pay — pays for an order with PayPal, using either one-off card details
/// or one of the shopper's saved cards. Idempotent in effect: paying an already-paid order returns
/// the existing payment rather than charging again.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                PayOrderBody body,
                ClaimsPrincipal user,
                IOrderPaymentService orderPaymentService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                body ??= new PayOrderBody();
                var instruction = new PaymentInstruction(body.Card?.ToCardDetails(), body.SavedPaymentMethodId);

                var order = await orderPaymentService.PayOrderAsync(buyerId, orderId, instruction, cancellationToken);
                if (order is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(new PayOrderResponse
                {
                    OrderId = order.Id,
                    PaymentStatus = order.PaymentStatus.ToString(),
                    Amount = order.Total(),
                    Currency = OrderMapping.Currency,
                    PayPalOrderId = order.PayPalOrderId,
                    CaptureId = order.PayPalCaptureId
                });
            })
            .Produces<PayOrderResponse>()
            .WithTags("PaymentEndpoints");
    }
}
