using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing order/order-item model. The shopper is told (by SMS) that their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user,
             IRepository<Order> orderRepository, IRepository<CatalogItem> itemRepository,
             IUriComposer uriComposer, INotificationService notifications) =>
            {
                var buyerId = user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (request?.Items is null || request.Items.Count == 0)
                    return Results.BadRequest(new { message = "At least one order item is required." });
                if (request.Items.Any(i => i.Quantity <= 0))
                    return Results.BadRequest(new { message = "Every item quantity must be greater than zero." });

                var catalogItemIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
                var catalogItems = await itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds));
                var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
                if (missing.Length > 0)
                    return Results.BadRequest(new { message = $"Unknown catalog item id(s): {string.Join(", ", missing)}." });

                var items = request.Items.Select(requested =>
                {
                    var catalogItem = catalogItems.First(c => c.Id == requested.CatalogItemId);
                    var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, uriComposer.ComposePicUri(catalogItem.PictureUri));
                    return new OrderItem(itemOrdered, catalogItem.Price, requested.Quantity);
                }).ToList();

                var order = new Order(buyerId, ToAddress(request.ShipToAddress), items);
                order = await orderRepository.AddAsync(order);

                // A message that cannot be sent never fails the order: notification handling swallows its own errors.
                await notifications.NotifyOrderPlacedAsync(order);

                return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                    Total = order.Total()
                });
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    // ShipToAddress is required by the order model; default a placeholder when the caller supplies none.
    private static Address ToAddress(AddressDto? dto) => dto is null
        ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
        : new Address(
            dto.Street ?? "N/A", dto.City ?? "N/A", dto.State ?? "N/A", dto.Country ?? "N/A", dto.ZipCode ?? "00000");
}

public class CreateOrderRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderResponse
{
    /// <summary>Identifier of the placed order.</summary>
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}
