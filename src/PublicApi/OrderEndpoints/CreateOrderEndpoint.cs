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
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog items, reusing the existing order model.
/// The shopper is told (by SMS) that their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IShopperOrderService service, CancellationToken cancellationToken) =>
            {
                var buyerId = CurrentUser.GetUserName(user);
                if (buyerId is null)
                {
                    return Results.Unauthorized();
                }

                var lines = (request?.Items ?? new())
                    .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
                    .ToList();

                var result = await service.PlaceOrderAsync(buyerId, lines, cancellationToken);
                if (!result.Succeeded)
                {
                    var message = result.Error switch
                    {
                        PlaceOrderError.NoItems => "An order must contain at least one item with a positive quantity.",
                        PlaceOrderError.ItemNotFound => "One or more catalog items could not be found.",
                        _ => "The order could not be placed."
                    };
                    return Results.BadRequest(new { message });
                }

                var response = new CreateOrderResponse(request!.CorrelationId())
                {
                    OrderId = result.OrderId!.Value
                };

                return Results.Created($"api/orders/{response.OrderId}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
