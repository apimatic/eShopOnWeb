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

/// <summary>
/// Shopper endpoint: POST /api/orders
/// Places an order from catalog item ids and quantities (reusing the Order aggregate) and takes
/// the ordered quantities off eShop's own stock. An order for more of an item than eShop holds is
/// refused. Available to any signed-in shopper.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPlacementService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPlacementService orderService, CancellationToken ct) =>
            {
                request.BuyerId = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
                return await HandleAsync(request, orderService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPlacementService orderService)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.BadRequest(new { message = "Unable to determine the buyer identity from the token." });
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "An order must contain at least one item." });
        }

        var lines = request.Items
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var address = request.ShipToAddress is { } a
            ? new Address(a.Street, a.City, a.State, a.Country, a.ZipCode)
            : DefaultShippingAddress();

        var orderId = await orderService.PlaceOrderAsync(request.BuyerId, lines, address);

        return Results.Created($"api/orders/{orderId}", new CreateOrderResponse { OrderId = orderId });
    }

    // POST /api/orders accepts only item ids and quantities; when no ship-to address is supplied
    // the Order aggregate still requires one, so a placeholder is used.
    private static Address DefaultShippingAddress() =>
        new("123 Main St.", "Kent", "OH", "United States", "44240");
}

public class CreateOrderRequest
{
    /// <summary>Set from the authenticated identity, not from the request body.</summary>
    public string BuyerId { get; set; } = string.Empty;

    public List<CreateOrderItem> Items { get; set; } = new();

    /// <summary>Optional ship-to address; a placeholder is used when omitted.</summary>
    public CreateOrderAddress? ShipToAddress { get; set; }
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderAddress
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
}
