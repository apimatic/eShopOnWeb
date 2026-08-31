using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper and notifies them by text message.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal, IOrderPlacementService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPlacementService orderPlacementService) =>
            {
                return await HandleAsync(request, user, orderPlacementService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user, IOrderPlacementService orderPlacementService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        Address? shipTo = request.ShipToAddress == null
            ? null
            : new Address(request.ShipToAddress.Street ?? "Not provided",
                request.ShipToAddress.City ?? "Not provided",
                request.ShipToAddress.State ?? "Not provided",
                request.ShipToAddress.Country ?? "Not provided",
                request.ShipToAddress.ZipCode ?? "00000");

        var items = request.Items.Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity)).ToList();
        var result = await orderPlacementService.PlaceOrderAsync(buyerId, items, shipTo);
        if (!result.Success)
        {
            return Results.BadRequest(new { error = result.Error });
        }

        var order = result.Order!;
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        return Results.Created($"api/orders/{response.OrderId}", response);
    }
}
