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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public List<PlaceOrderItemDto> Items { get; set; } = new();
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids/quantities. The order starts
/// awaiting payment - pay it with POST api/orders/{orderId}/pay.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderService>
{
    // PublicApi collects no shipping address today (no storefront checkout form on this surface);
    // reuses the same placeholder address the Web storefront's checkout uses.
    private static readonly Address PlaceholderShippingAddress = new("123 Main St.", "Kent", "OH", "United States", "44240");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderService orderService) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, orderService);
            })
            .Produces<PlaceOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderService orderService)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }

        var response = new PlaceOrderResponse(request.CorrelationId());

        var items = request.Items.Select(i => (i.CatalogItemId, i.Quantity)).ToList();
        var order = await orderService.CreateOrderFromItemsAsync(request.BuyerId, PlaceholderShippingAddress, items);

        response.OrderId = order.Id;
        response.Order = OrderDto.FromEntity(order);
        return Results.Created($"api/my-orders/{order.Id}", response);
    }
}
