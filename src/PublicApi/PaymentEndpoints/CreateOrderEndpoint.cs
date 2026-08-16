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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreateOrderRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper.
/// The order starts awaiting payment. Returns the order id as a top-level field.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderEndpoint.Request, IOrderPlacementService>
{
    public record Request(string BuyerId, CreateOrderRequest Body);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPlacementService placementService) =>
                await HandleAsync(new Request(user.GetBuyerId(), request ?? new CreateOrderRequest()), placementService))
            .Produces<OrderSummaryDto>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, IOrderPlacementService placementService)
    {
        var lines = (request.Body.Items ?? new List<CreateOrderItem>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await placementService.PlaceOrderAsync(request.BuyerId, lines);
        var dto = PaymentDtoMapper.ToDto(order);
        return Results.Created($"api/orders/{order.Id}", dto);
    }
}
