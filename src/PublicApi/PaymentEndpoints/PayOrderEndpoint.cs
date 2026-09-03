using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total, using one-off card details or a
/// saved card. Money is held, not taken. Shopper-scoped: acts only on the caller's own order.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                PayOrderRequest request,
                IPaymentOrderService service,
                PayPalSettings settings,
                HttpContext http,
                System.Threading.CancellationToken ct) =>
            {
                var buyerId = http.User.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var card = request.Card?.ToCardDetails();
                var order = await service.AuthorizeAsync(buyerId, orderId, card, request.SavedCardId, ct);
                return Results.Ok(OrderPaymentResponse.From(order, settings.Currency));
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("PaymentEndpoints");
    }
}
