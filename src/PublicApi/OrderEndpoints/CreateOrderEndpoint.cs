using System;
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
using Microsoft.eShopWeb.PublicApi.SmsNotifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper, reusing the app's existing
/// order/order-item model. The shopper is told their order was placed. A messaging failure
/// never fails the order.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                ClaimsPrincipal user,
                IRepository<CatalogItem> catalogRepository,
                IRepository<Order> orderRepository,
                IUriComposer uriComposer,
                IOrderNotificationService notifications) =>
            {
                var ownerId = user.GetOwnerId();
                if (string.IsNullOrEmpty(ownerId))
                {
                    return Results.Unauthorized();
                }

                if (request?.Items is null || request.Items.Count == 0)
                {
                    return Results.BadRequest(new { message = "An order needs at least one item." });
                }

                if (request.Items.Any(i => i.Quantity <= 0))
                {
                    return Results.BadRequest(new { message = "Every item needs a quantity greater than zero." });
                }

                var ids = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
                var catalogItems = await catalogRepository.ListAsync(new CatalogItemsSpecification(ids));

                var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToList();
                if (missing.Count > 0)
                {
                    return Results.BadRequest(new { message = $"Unknown catalog item id(s): {string.Join(", ", missing)}." });
                }

                var orderItems = new List<OrderItem>();
                foreach (var line in request.Items)
                {
                    var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
                    var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, uriComposer.ComposePicUri(catalogItem.PictureUri));
                    orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
                }

                var order = new Order(ownerId, NotificationApiHelpers.DefaultShippingAddress(), orderItems);
                await orderRepository.AddAsync(order);

                // Tell the shopper their order was placed — but never let a messaging problem
                // fail the order that was successfully written.
                try
                {
                    await notifications.NotifyOrderPlacedAsync(order);
                }
                catch
                {
                    // Swallowed by design; the notification service logs the detail internally.
                }

                return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                    Total = order.Total(),
                    Items = order.OrderItems.Select(i => new CreateOrderResponseItem
                    {
                        CatalogItemId = i.ItemOrdered.CatalogItemId,
                        ProductName = i.ItemOrdered.ProductName,
                        UnitPrice = i.UnitPrice,
                        Units = i.Units
                    }).ToList()
                });
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }
}

public class CreateOrderRequest
{
    public List<CreateOrderLine> Items { get; set; } = new();
}

public class CreateOrderLine
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse
{
    /// <summary>Identifier of the created order (top-level, so the flow can be driven end to end).</summary>
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<CreateOrderResponseItem> Items { get; set; } = new();
}

public class CreateOrderResponseItem
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
