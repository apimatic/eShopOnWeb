using System.Linq;
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

/// <summary>Places an order for the signed-in shopper from catalog items; it starts awaiting payment.</summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService,
                CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var lines = (request.Items ?? new())
                    .Select(i => new OrderLineInput(i.CatalogItemId, i.Units))
                    .ToList();

                var order = await paymentService.PlaceOrderAsync(buyerId, lines,
                    request.ShipToAddress.ToAddress(), ct);

                var response = OrderResponse.FromOrder(order);
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
