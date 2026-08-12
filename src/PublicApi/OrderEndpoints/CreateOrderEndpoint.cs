using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationsFeature;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper, reusing
/// eShop's existing Order / OrderItem model. The shopper is told their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                [FromBody] CreateOrderRequest request,
                ClaimsPrincipal user,
                IRepository<Order> orderRepository,
                IRepository<OrderStatusRecord> statusRepository,
                IReadRepository<CatalogItem> catalogRepository,
                IUriComposer uriComposer,
                IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrWhiteSpace(buyerId))
                    return Results.Unauthorized();

                if (request?.Items is null || request.Items.Count == 0)
                    return Results.BadRequest(new { message = "At least one order item is required." });

                if (request.Items.Any(i => i.Quantity <= 0))
                    return Results.BadRequest(new { message = "Every item quantity must be greater than zero." });

                var itemIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
                var catalogItems = await catalogRepository.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);
                var catalogById = catalogItems.ToDictionary(c => c.Id);

                var missing = itemIds.Where(id => !catalogById.ContainsKey(id)).ToArray();
                if (missing.Length > 0)
                    return Results.BadRequest(new { message = $"Unknown catalog item id(s): {string.Join(", ", missing)}." });

                // Build the order with the existing domain model (mirrors OrderService mapping).
                var orderItems = request.Items.Select(line =>
                {
                    var catalogItem = catalogById[line.CatalogItemId];
                    var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, uriComposer.ComposePicUri(catalogItem.PictureUri));
                    return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
                }).ToList();

                var address = ToAddress(request.ShipToAddress);
                var order = new Order(buyerId, address, orderItems);
                await orderRepository.AddAsync(order, cancellationToken);

                var statusRecord = new OrderStatusRecord(order.Id, buyerId);
                await statusRepository.AddAsync(statusRecord, cancellationToken);

                // Best-effort: never fails the placement.
                await notificationService.NotifyOrderPlacedAsync(order, cancellationToken);

                var response = new CreateOrderResponse
                {
                    OrderId = order.Id,
                    State = statusRecord.State.ToString(),
                    Total = order.Total()
                };
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    private static Address ToAddress(ShipToAddressRequest? request)
    {
        if (request is not null &&
            !string.IsNullOrWhiteSpace(request.Street) &&
            !string.IsNullOrWhiteSpace(request.City) &&
            !string.IsNullOrWhiteSpace(request.Country) &&
            !string.IsNullOrWhiteSpace(request.ZipCode))
        {
            return new Address(request.Street, request.City, request.State ?? string.Empty, request.Country, request.ZipCode);
        }

        // A shipping address is not the focus of this feature; supply a sensible default when omitted.
        return new Address("123 Main Street", "Redmond", "WA", "USA", "98052");
    }
}
