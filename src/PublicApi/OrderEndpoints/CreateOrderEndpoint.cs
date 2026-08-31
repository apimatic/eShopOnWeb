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
using Microsoft.eShopWeb.PublicApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the authenticated shopper from catalog item ids and quantities, reusing the
/// app's existing Order/OrderItem model. The caller's identity comes from the token.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                ClaimsPrincipal user,
                IOrderPlacementService orderPlacementService,
                CancellationToken cancellationToken) =>
            {
                var lines = (request.Items ?? new())
                    .Select(item => new OrderLineRequest(item.CatalogItemId, item.Quantity));

                var order = await orderPlacementService.PlaceOrderAsync(user.BuyerId(), lines, cancellationToken);

                var response = new CreateOrderResponse(request.CorrelationId())
                {
                    OrderId = order.Id,
                    Total = order.Total(),
                    ItemCount = order.OrderItems.Count
                };

                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
