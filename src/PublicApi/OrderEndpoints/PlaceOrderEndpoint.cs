using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing order/order-item model. The shopper is told their order was placed.
/// </summary>
public class PlaceOrderEndpoint : AuthenticatedEndpointBase,
    IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService>
{
    public PlaceOrderEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
    {
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderNotificationService service) =>
                await HandleAsync(request, service))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service)
    {
        if (request is null || request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }

        var lines = request.Items
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await service.PlaceOrderAsync(BuyerId, lines, request.ShipToAddress?.ToAddress(), RequestAborted);

        var response = new PlaceOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Items = order.OrderItems.Select(oi => new OrderItemDto
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
