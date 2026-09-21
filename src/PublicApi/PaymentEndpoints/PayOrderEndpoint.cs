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
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total, funded by a one-off card or a
/// saved card. Does not take the money. Shopper-scoped and idempotent in effect.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderBody body, IOrderPaymentService service, HttpContext http) =>
                await HandleAsync(new PayOrderRequest(orderId, body?.Card, body?.SavedPaymentMethodId), service, http))
            .Produces<OrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service, HttpContext http)
    {
        var buyerId = http.BuyerId();
        var card = request.Card?.ToCardDetails();
        var order = await service.PayAsync(buyerId, request.OrderId, card, request.SavedPaymentMethodId, http.RequestAborted);
        return Results.Ok(order.ToResponse());
    }
}
