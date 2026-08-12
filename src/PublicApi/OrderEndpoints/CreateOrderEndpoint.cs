using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing Order/OrderItem model. The shopper is then told their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext>
{
    // The API collects only items; a placeholder shipping address satisfies the existing Order model.
    private static readonly Address PlaceholderShipToAddress =
        new("Not provided", "Not provided", "NA", "Not provided", "00000");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext httpContext)
    {
        var cancellationToken = httpContext.RequestAborted;
        var buyerId = httpContext.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest(new { message = "An order must contain at least one item." });

        var catalogRepository = httpContext.RequestServices.GetRequiredService<IRepository<CatalogItem>>();
        var orderRepository = httpContext.RequestServices.GetRequiredService<IRepository<Order>>();
        var notificationService = httpContext.RequestServices.GetRequiredService<IOrderNotificationService>();

        var orderItems = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            if (line.Quantity <= 0)
                return Results.BadRequest(new { message = $"Quantity for catalog item {line.CatalogItemId} must be greater than zero." });

            var catalogItem = await catalogRepository.GetByIdAsync(line.CatalogItemId, cancellationToken);
            if (catalogItem is null)
                return Results.BadRequest(new { message = $"Catalog item {line.CatalogItemId} does not exist." });

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, PlaceholderShipToAddress, orderItems);
        await orderRepository.AddAsync(order, cancellationToken);

        // Tell the shopper the order was placed. A messaging failure must not fail the placement.
        await notificationService.NotifyOrderPlacedAsync(order, cancellationToken);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
