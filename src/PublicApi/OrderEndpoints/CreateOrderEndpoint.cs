using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; set; }

    [Range(1, 1000)]
    public int Quantity { get; set; }
}

public class CreateOrderRequest
{
    [Required, MinLength(1)]
    public List<CreateOrderItemRequest> Items { get; set; } = new();

    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper and tells them
/// (by SMS, if they have a number on file) that the order was placed.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateOrderEndpoint : EndpointBaseAsync
    .WithRequest<CreateOrderRequest>
    .WithActionResult<CreateOrderResponse>
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IOrderNotificationService _notifications;

    public CreateOrderEndpoint(IRepository<Order> orders, IRepository<CatalogItem> catalogItems,
        IOrderNotificationService notifications)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _notifications = notifications;
    }

    [HttpPost("api/orders")]
    [SwaggerOperation(Summary = "Places an order for the caller", Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<CreateOrderResponse>> HandleAsync(
        [FromBody] CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        if (buyerId is null) return Unauthorized();

        var orderItems = new List<OrderItem>();
        foreach (var item in request.Items)
        {
            var catalogItem = await _catalogItems.GetByIdAsync(item.CatalogItemId, cancellationToken);
            if (catalogItem is null)
            {
                return NotFound(new { error = $"Catalog item {item.CatalogItemId} does not exist." });
            }

            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri) ? "eCatalog-item-default.png" : catalogItem.PictureUri;
            orderItems.Add(new OrderItem(new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri),
                catalogItem.Price, item.Quantity));
        }

        var address = new Address(request.Street, request.City, request.State, request.Country, request.ZipCode);
        var order = await _orders.AddAsync(new Order(buyerId, address, orderItems), cancellationToken);

        // Never fails the order: notification errors are recorded, not thrown.
        await _notifications.NotifyOrderPlacedAsync(order, cancellationToken);

        return new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
    }
}
