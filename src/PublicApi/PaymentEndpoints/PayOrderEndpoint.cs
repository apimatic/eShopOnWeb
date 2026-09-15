using System.Security.Claims;
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
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total using one-off card details or one
/// of the shopper's saved cards. Does not take the money. Idempotent: a double-click never authorizes
/// twice. Shopper-scoped: acts only on the caller's own order.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                request.CallerBuyerId = user.GetBuyerId();
                request.CallerIsAdmin = user.IsAdministrator();
                return await HandleAsync(request, service);
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        var card = request.Card?.ToCardDetails();
        var order = await service.AuthorizeOrderAsync(request.CallerBuyerId, request.OrderId, card, request.SavedCardId);
        return Results.Ok(order.ToResponse());
    }
}
