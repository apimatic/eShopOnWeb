using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequestItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest
{
    public List<CreateOrderRequestItem> Items { get; set; } = new();
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
}

/// <summary>
/// Places an order from catalog items for the authenticated shopper, reusing the app's existing
/// order/order-item model. The buyer is taken from the token, never from the request body.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext>
{
    private readonly IOrderPlacementService _orderPlacementService;

    public CreateOrderEndpoint(IOrderPlacementService orderPlacementService)
    {
        _orderPlacementService = orderPlacementService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var lines = (request.Items ?? new List<CreateOrderRequestItem>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var result = await _orderPlacementService.PlaceOrderAsync(buyerId, lines);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new { error = result.Error });
        }

        var response = new CreateOrderResponse { OrderId = result.OrderId!.Value };
        return Results.Created($"api/orders/{response.OrderId}", response);
    }
}
