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

/// <summary>Authorizes (holds) the order total using one-off card details or a saved card.</summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId, PayOrderRequest request, ClaimsPrincipal user,
                IPaymentService paymentService, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var card = request.Card?.ToCardDetails();
                var order = await paymentService.AuthorizeAsync(orderId, buyerId, card,
                    request.PaymentMethodId, ct);
                return Results.Ok(OrderResponse.FromOrder(order));
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
