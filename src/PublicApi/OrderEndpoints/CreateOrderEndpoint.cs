using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
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

/// <summary>
/// Places an order from catalog items for the signed-in shopper, reusing the app's existing order model.
/// The shopper is told their order was placed. Returns the new order's id.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPlacementService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPlacementService service) =>
            {
                request.Caller = CallerIdentity.GetUserName(user);
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPlacementService service)
    {
        if (string.IsNullOrEmpty(request.Caller))
            return Results.Unauthorized();
        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest(new { error = "An order must contain at least one item." });

        var lines = request.Items.Select(i => new OrderLineItem(i.CatalogItemId, i.Quantity)).ToList();

        try
        {
            var order = await service.PlaceOrderAsync(request.Caller, lines);
            var response = new CreateOrderResponse
            {
                OrderId = order.Id,
                Total = order.Total(),
                ItemCount = order.OrderItems.Count
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (CatalogItemNotFoundException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (EmptyOrderException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateOrderRequest
{
    /// <summary>The catalog items and quantities to order.</summary>
    public List<CreateOrderItem> Items { get; set; } = new();

    [JsonIgnore]
    public string? Caller { get; set; }
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse
{
    /// <summary>The identifier of the order that was placed.</summary>
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
}
