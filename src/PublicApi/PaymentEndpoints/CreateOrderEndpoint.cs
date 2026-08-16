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
/// POST /api/orders — a logged-in shopper places an order from catalog items and quantities.
/// The order starts awaiting payment. Amounts come from catalog prices, never from the caller.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
            {
                var buyerId = RequestMapper.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var order = await service.PlaceOrderAsync(buyerId, RequestMapper.ToPlaceOrderInput(request), ct);
                return Results.Created($"api/orders/{order.Id}",
                    new { orderId = order.Id, order = PaymentMapper.ToDto(order) });
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
