using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderRequest : BaseRequest
{
    /// <summary>The catalog items and quantities to order.</summary>
    public List<OrderItemRequest> Items { get; set; } = new();

    /// <summary>Optional shipping address; a placeholder is used when omitted.</summary>
    public AddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>The id of the placed order — a top-level field so the flow can be driven end to end.</summary>
    public int OrderId { get; set; }

    public OrderDto? Order { get; set; }
}

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing order/order-item model. On success the shopper is told their order was placed (best-effort;
/// a messaging failure never fails the order). The buyer is the caller identified by the token.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request,
             ClaimsPrincipal user,
             IRepository<Order> orderRepository,
             IReadRepository<CatalogItem> itemRepository,
             IUriComposer uriComposer,
             IOrderNotificationService notifications) =>
            {
                var buyerId = user.GetUserId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (request.Items is null || request.Items.Count == 0)
                    return Results.BadRequest("An order must contain at least one item.");
                if (request.Items.Any(i => i.Quantity <= 0))
                    return Results.BadRequest("Every item quantity must be greater than zero.");

                var catalogItemIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
                var catalogItems = await itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds));
                var byId = catalogItems.ToDictionary(c => c.Id);

                var missing = catalogItemIds.Where(id => !byId.ContainsKey(id)).ToArray();
                if (missing.Length > 0)
                    return Results.BadRequest($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

                // Build order items as snapshots of the catalog items, mirroring the existing order flow.
                var orderItems = request.Items.Select(line =>
                {
                    var catalogItem = byId[line.CatalogItemId];
                    var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, uriComposer.ComposePicUri(catalogItem.PictureUri));
                    return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
                }).ToList();

                var address = BuildAddress(request.ShipToAddress);
                var order = new Order(buyerId, address, orderItems);
                await orderRepository.AddAsync(order);

                // Tell the shopper their order was placed. Never lets a messaging failure fail the order.
                await notifications.NotifyOrderPlacedAsync(order);

                var response = new CreateOrderResponse(request.CorrelationId())
                {
                    OrderId = order.Id,
                    Order = OrderDto.From(order)
                };
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    private static Address BuildAddress(AddressRequest? request)
    {
        if (request is null)
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");

        return new Address(
            NullIfEmpty(request.Street) ?? "N/A",
            NullIfEmpty(request.City) ?? "N/A",
            request.State ?? string.Empty,
            NullIfEmpty(request.Country) ?? "N/A",
            NullIfEmpty(request.ZipCode) ?? "00000");
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
