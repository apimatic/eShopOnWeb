using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders — place an order from catalog item ids and quantities, reusing the app's existing
/// order/order-item model. The buyer is the caller (from the token). The shopper is told their order was placed.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, OrderEndpointServices>
{
    // Placeholder used when the caller does not supply a shipping address; SMS notifications are the focus here.
    private static readonly Address PlaceholderAddress = new("N/A", "N/A", "N/A", "N/A", "00000");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, OrderEndpointServices services) => await HandleAsync(request, services))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, OrderEndpointServices services)
    {
        var buyerId = services.User.UserName();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest(new { message = "At least one order item is required." });

        if (request.Items.Any(i => i.Quantity <= 0))
            return Results.BadRequest(new { message = "Every item quantity must be greater than zero." });

        var catalogItemIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await services.CatalogItems.ListAsync(new CatalogItemsSpecification(catalogItemIds));

        var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            return Results.BadRequest(new { message = $"Unknown catalog item id(s): {string.Join(", ", missing)}." });

        var orderItems = request.Items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, services.UriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var shipToAddress = request.ShipToAddress?.ToAddress() ?? PlaceholderAddress;
        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await services.Orders.AddAsync(order);

        // Tell the shopper their order was placed. A messaging failure must not fail the order.
        await services.Notifier.NotifyOrderPlacedAsync(order);

        var response = new PlaceOrderResponse { OrderId = order.Id, Status = order.Status.ToString() };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
