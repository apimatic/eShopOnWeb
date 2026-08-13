using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.SmsNotifications;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
}

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing Order / OrderItem model. The shopper is then told their order was placed; a messaging
/// failure never fails the placement.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext>
{
    // eShopOnWeb has no shipping-address capture on this API surface; reuse the storefront's default.
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext http)
    {
        var ownerId = CallerIdentity.GetOwnerId(http.User);
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest(new { error = "At least one order item is required." });

        if (request.Items.Any(i => i.Quantity <= 0))
            return Results.BadRequest(new { error = "Every item quantity must be greater than zero." });

        var itemRepository = http.RequestServices.GetRequiredService<IRepository<CatalogItem>>();
        var orderRepository = http.RequestServices.GetRequiredService<IRepository<Order>>();
        var uriComposer = http.RequestServices.GetRequiredService<IUriComposer>();
        var notifications = http.RequestServices.GetRequiredService<IOrderNotificationService>();

        var requestedIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await itemRepository.ListAsync(new CatalogItemsSpecification(requestedIds), http.RequestAborted);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = requestedIds.Where(id => !catalogById.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            return Results.BadRequest(new { error = $"Unknown catalog item id(s): {string.Join(", ", missing)}." });

        var orderItems = request.Items.Select(line =>
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(ownerId, DefaultShipToAddress, orderItems);
        await orderRepository.AddAsync(order, http.RequestAborted);

        await notifications.NotifyOrderPlacedAsync(order, http.RequestAborted);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
