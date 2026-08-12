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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderAddressRequest
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

    // Optional; the notification flow does not depend on a shipping address, so a placeholder is used
    // when none is supplied.
    public CreateOrderAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderResponse
{
    // Top-level identifier of the created order.
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing order/order-item model, and tells the shopper their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user,
                IRepository<Order> orderRepository, IReadRepository<CatalogItem> itemRepository,
                INotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, orderRepository, itemRepository, notificationService, cancellationToken);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user,
        IRepository<Order> orderRepository, IReadRepository<CatalogItem> itemRepository,
        INotificationService notificationService, CancellationToken cancellationToken)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var lineItems = request.Items?.Where(i => i.Quantity > 0).ToList() ?? new List<CreateOrderItemRequest>();
        if (lineItems.Count == 0)
        {
            return Results.BadRequest(new { message = "An order must contain at least one item with a positive quantity." });
        }

        var ids = lineItems.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            return Results.BadRequest(new { message = $"Unknown catalog item id(s): {string.Join(", ", missing)}." });
        }

        var orderItems = lineItems.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri) ? "eCatalog-item-default.png" : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = BuildAddress(request.ShipToAddress);
        var order = new Order(buyerId, address, orderItems);
        await orderRepository.AddAsync(order, cancellationToken);

        // Tell the shopper their order was placed. Never fails the order.
        await notificationService.NotifyOrderPlacedAsync(order, cancellationToken);

        var response = new CreateOrderResponse { OrderId = order.Id, Status = order.Status.ToString() };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address BuildAddress(CreateOrderAddressRequest? address)
    {
        if (address is null)
        {
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");
        }
        return new Address(
            NullIfEmpty(address.Street) ?? "N/A",
            NullIfEmpty(address.City) ?? "N/A",
            NullIfEmpty(address.State) ?? "N/A",
            NullIfEmpty(address.Country) ?? "N/A",
            NullIfEmpty(address.ZipCode) ?? "00000");
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
