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

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the
/// app's existing Order / OrderItem model. The shopper is then told their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    // PublicApi does not collect a shipping address; reuse the sample's default, as the
    // storefront checkout does.
    private static Address DefaultShipToAddress() =>
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                IRepository<CatalogItem> catalogRepository,
                IRepository<Order> orderRepository,
                IUriComposer uriComposer,
                IOrderNotificationService notificationService,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var lineItems = (request.Items ?? new List<CreateOrderItem>())
                    .Where(i => i.Quantity > 0)
                    .GroupBy(i => i.CatalogItemId)
                    .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(i => i.Quantity) })
                    .ToList();

                if (lineItems.Count == 0)
                {
                    return Results.BadRequest(new { error = "An order must contain at least one item with a positive quantity." });
                }

                var ids = lineItems.Select(i => i.CatalogItemId).ToArray();
                var catalogItems = await catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
                var catalogById = catalogItems.ToDictionary(c => c.Id);

                var missing = ids.Where(id => !catalogById.ContainsKey(id)).ToArray();
                if (missing.Length > 0)
                {
                    return Results.BadRequest(new { error = $"Unknown catalog item id(s): {string.Join(", ", missing)}." });
                }

                var orderItems = lineItems.Select(line =>
                {
                    var catalogItem = catalogById[line.CatalogItemId];
                    var itemOrdered = new CatalogItemOrdered(
                        catalogItem.Id, catalogItem.Name, uriComposer.ComposePicUri(catalogItem.PictureUri));
                    return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
                }).ToList();

                var order = new Order(buyerId, DefaultShipToAddress(), orderItems);
                order = await orderRepository.AddAsync(order, cancellationToken);

                // Tell the shopper their order was placed. A messaging failure never fails this.
                await notificationService.NotifyOrderPlacedAsync(order, cancellationToken);

                var response = new CreateOrderResponse
                {
                    OrderId = order.Id,
                    Status = "placed",
                    Total = order.Total(),
                    ItemCount = orderItems.Sum(i => i.Units)
                };
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }
}
