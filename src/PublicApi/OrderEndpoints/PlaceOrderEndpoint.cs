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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids + quantities, reusing the app's existing
/// order model, and tells the shopper their order was placed.
/// </summary>
public class PlaceOrderEndpoint
    : IEndpoint<IResult, PlaceOrderRequest, IShopperOrderService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IShopperOrderService service, CancellationToken ct) =>
            {
                request.BuyerId = user.Identity?.Name;
                return await HandleAsync(request, service, ct);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IShopperOrderService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var lines = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        try
        {
            var orderId = await service.PlaceOrderAsync(request.BuyerId!, lines, ct);
            return Results.Created($"api/orders/{orderId}", new PlaceOrderResponse { OrderId = orderId });
        }
        catch (InvalidOrderRequestException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class PlaceOrderRequest : BaseRequest
{
    public List<OrderLineDto>? Items { get; set; }
    public string? BuyerId { get; set; }
}

public sealed class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
}
