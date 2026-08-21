using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total, with a one-off card or a saved card.
/// Does not capture. Idempotent per order.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext http, IOrderPaymentService service) =>
            {
                var buyerId = http.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var card = request.Card is null ? null : PaymentMappers.ToCardInput(request.Card);
                var instruction = new PayInstruction(card, request.SavedPaymentMethodId);

                var result = await service.PayAsync(buyerId, orderId, instruction, http.RequestAborted);
                return result.ToApiResult(Results.Ok);
            })
            .Produces<PaymentView>()
            .WithTags("PaymentOrderEndpoints");
    }
}
