using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total. The request carries either raw
/// card details or the id of one of the shopper's saved cards. Idempotent: a double-click never
/// authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentProcessingService service, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                var instrument = new PaymentInstrument(request.Card?.ToCardDetails(), request.SavedCardId);

                var order = await service.AuthorizeOrderAsync(buyerId, orderId, instrument, ct);
                return Results.Ok(OrderPresentation.ToDto(order));
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }
}
