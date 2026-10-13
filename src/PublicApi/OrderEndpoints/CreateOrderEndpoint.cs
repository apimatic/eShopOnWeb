using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>One requested line: a catalog item id and how many to buy.</summary>
public class CreateOrderItemDto
{
    public int CatalogItemId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
}

/// <summary>Body of POST /api/orders.</summary>
public class CreateOrderRequest
{
    [Required]
    [MinLength(1)]
    public List<CreateOrderItemDto> Items { get; set; } = new();
}

/// <summary>Response: the new order id (top-level).</summary>
public record CreateOrderResponse(int OrderId);

/// <summary>
/// Places an order for the given catalog items and quantities, taking them off eShop's stock.
/// Refused if an item is ordered beyond what eShop holds. Reuses the existing order aggregate.
/// Available to any signed-in shopper.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICatalogStockService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, ICatalogStockService stock, CancellationToken ct) =>
            {
                var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
                return await HandleAsync(request, buyerId, stock, ct);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, ICatalogStockService stock)
        => HandleAsync(request, buyerId: null, stock, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateOrderRequest request, string? buyerId, ICatalogStockService stock, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "An order must contain at least one item." });
        }

        var lines = request.Items
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        try
        {
            var order = await stock.CreateOrderAsync(buyerId, lines, ct);
            return Results.Ok(new CreateOrderResponse(order.Id));
        }
        catch (InsufficientStockException ex)
        {
            // The order is refused (over-order or unknown item).
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}
