using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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

public class CreateOrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest
{
    public List<CreateOrderItemDto> Items { get; set; } = new();

    /// <summary>Set from the authenticated identity, not the body.</summary>
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
}

/// <summary>
/// POST /api/orders — shopper action: places an order from catalog item ids and quantities, taking
/// the ordered quantities off eShop's own stock. Refused if any line exceeds stock. Any signed-in user.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderService orderService, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request ??= new CreateOrderRequest();
                request.BuyerId = user.Identity?.Name
                    ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? "public-api-shopper";
                return await HandleAsync(request, orderService, ct);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService)
        => HandleAsync(request, orderService, default);

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "An order must contain at least one item." });
        }
        if (request.Items.Any(i => i.CatalogItemId <= 0))
        {
            return Results.BadRequest(new { message = "Each item must have a valid catalogItemId." });
        }
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "Each item quantity must be greater than zero." });
        }

        var lines = request.Items
            .Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        // The task payload carries only item ids and quantities; supply a placeholder ship-to address,
        // which the existing Order aggregate requires.
        var shippingAddress = new Address("N/A", "N/A", "N/A", "N/A", "00000");

        var order = await orderService.CreateOrderAsync(request.BuyerId, lines, shippingAddress);

        return Results.Ok(new CreateOrderResponse { OrderId = order.Id });
    }
}
