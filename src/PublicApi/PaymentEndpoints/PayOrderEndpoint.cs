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

/// <summary>Authorizes (holds) the order total using a one-off card or one of the shopper's saved cards.</summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                PayOrderRequest request,
                ClaimsPrincipal user,
                IOrderPaymentService service,
                CancellationToken ct) =>
            {
                var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
                var view = await service.PayAsync(buyerId, orderId, request.Card.ToDomain(), request.SavedCardId, ct);
                return Results.Ok(view);
            })
            .Produces<OrderPaymentView>()
            .WithTags("PaymentEndpoints");
    }
}
