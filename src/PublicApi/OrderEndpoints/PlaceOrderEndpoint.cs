using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderItem> Items { get; set; } = new();

    /// <summary>Set from the caller's token, not the request body.</summary>
    public string? BuyerId { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }
    public PlaceOrderResponse() { }

    public int OrderId { get; set; }
}

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing order model, and tells the shopper their order was placed.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPlacementService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderPlacementService service, ClaimsPrincipal user) =>
            {
                request.BuyerId = CallerIdentity.GetUserName(user);
                return await HandleAsync(request, service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPlacementService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var lines = (request.Items ?? new List<PlaceOrderItem>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var result = await service.PlaceOrderAsync(request.BuyerId, lines);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new { error = result.Error });
        }

        var response = new PlaceOrderResponse(request.CorrelationId()) { OrderId = result.OrderId };
        return Results.Created($"api/orders/{result.OrderId}", response);
    }
}
