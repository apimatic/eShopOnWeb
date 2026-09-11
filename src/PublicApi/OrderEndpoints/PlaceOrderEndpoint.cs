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

public class PlaceOrderRequest
{
    public List<PlaceOrderItem> Items { get; set; } = new();
}

public class PlaceOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderResponse
{
    /// <summary>The identifier of the created order.</summary>
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<PlaceOrderResponseItem> Items { get; set; } = new();
}

public class PlaceOrderResponseItem
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>
/// Places an order for the signed-in shopper from catalog items. The order starts awaiting payment.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
                await HandleAsync(request, user, service))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, ClaimsPrincipal user,
        IOrderPaymentService service)
    {
        var buyerId = CallerIdentity.GetBuyerId(user);
        var lines = (request.Items ?? new List<PlaceOrderItem>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await service.PlaceOrderAsync(buyerId, lines);

        var response = new PlaceOrderResponse
        {
            OrderId = order.Id,
            Status = "AwaitingPayment",
            Total = order.Total(),
            Items = order.OrderItems.Select(oi => new PlaceOrderResponseItem
            {
                CatalogItemId = oi.ItemOrdered.CatalogItemId,
                ProductName = oi.ItemOrdered.ProductName,
                UnitPrice = oi.UnitPrice,
                Units = oi.Units
            }).ToList()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
