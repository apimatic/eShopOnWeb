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
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total, with a one-off card or a
/// saved card. Idempotent in effect: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
            {
                var identity = CallerIdentity.Require(user);

                var hasCard = request.Card is not null;
                var hasSaved = request.SavedCardId is > 0;
                if (hasCard == hasSaved)
                {
                    return Results.BadRequest(new { message = "Provide exactly one of 'card' or 'savedCardId'." });
                }

                var order = hasSaved
                    ? await service.PayWithSavedCardAsync(identity, orderId, request.SavedCardId!.Value)
                    : await service.PayWithCardAsync(identity, orderId, request.Card!.ToCardDetails());

                return Results.Ok(order.ToDto());
            })
            .Produces<OrderDto>()
            .WithTags("OrderPaymentEndpoints");
    }
}
