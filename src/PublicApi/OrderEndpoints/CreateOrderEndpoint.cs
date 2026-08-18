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
/// POST /api/orders — place an order from catalog item ids + quantities, reusing the app's existing
/// Order/OrderItem model. The buyer is the token's identity. The shopper is then told their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                IRepository<Order> orderRepository,
                IRepository<CatalogItem> itemRepository,
                IOrderNotificationService notifications,
                IUriComposer uriComposer,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                if (request.Items is null || request.Items.Count == 0)
                {
                    return Results.BadRequest(new { message = "An order must contain at least one item." });
                }
                if (request.Items.Any(i => i.Quantity <= 0))
                {
                    return Results.BadRequest(new { message = "Every item quantity must be greater than zero." });
                }

                var ids = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
                var catalogItems = await itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
                if (catalogItems.Count != ids.Length)
                {
                    return Results.BadRequest(new { message = "One or more catalog items do not exist." });
                }

                var orderItems = request.Items.Select(line =>
                {
                    var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
                    var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, uriComposer.ComposePicUri(catalogItem.PictureUri));
                    return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
                }).ToList();

                var address = ToAddress(request.ShipToAddress);
                var order = new Order(buyerId, address, orderItems);
                await orderRepository.AddAsync(order, ct);

                // Best-effort: tell the shopper their order was placed. A send failure never fails the order.
                await notifications.NotifyOrderPlacedAsync(order, ct);

                return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse { OrderId = order.Id });
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    private static Address ToAddress(OrderAddressRequest? a)
    {
        if (a is null)
        {
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");
        }
        return new Address(
            string.IsNullOrWhiteSpace(a.Street) ? "N/A" : a.Street,
            string.IsNullOrWhiteSpace(a.City) ? "N/A" : a.City,
            string.IsNullOrWhiteSpace(a.State) ? "N/A" : a.State,
            string.IsNullOrWhiteSpace(a.Country) ? "N/A" : a.Country,
            string.IsNullOrWhiteSpace(a.ZipCode) ? "00000" : a.ZipCode);
    }
}
