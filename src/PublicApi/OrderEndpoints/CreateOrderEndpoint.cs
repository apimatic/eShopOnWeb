using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog item ids and quantities for the signed-in shopper,
/// then tells the shopper their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderApiService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderApiService orderService, CancellationToken cancellationToken) =>
            {
                request.BuyerId = user.GetBuyerId();
                request.CancellationToken = cancellationToken;
                return await HandleAsync(request, orderService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderApiService orderService)
    {
        if (request.BuyerId is null)
        {
            return Results.Unauthorized();
        }

        if (request.Items.Count == 0)
        {
            return BadRequest(request, "At least one item is required.");
        }

        if (request.Items.Any(i => i.Units <= 0 || i.CatalogItemId <= 0))
        {
            return BadRequest(request, "Every item needs a positive catalogItemId and units.");
        }

        var items = request.Items
            .Select(i => new OrderItemRequest(i.CatalogItemId, i.Units))
            .ToList();

        var order = await orderService.PlaceOrderAsync(
            request.BuyerId, items, request.ShipToAddress?.ToAddress(), request.CancellationToken);
        if (order is null)
        {
            return BadRequest(request, "One or more catalog item ids are unknown.");
        }

        return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                Units = i.Units,
                UnitPrice = i.UnitPrice
            }).ToList()
        });
    }

    private static IResult BadRequest(CreateOrderRequest request, string message) =>
        Results.BadRequest(new CreateOrderResponse(request.CorrelationId()) { Error = message });
}
