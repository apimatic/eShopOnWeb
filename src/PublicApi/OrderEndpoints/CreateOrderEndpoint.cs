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

public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IRepository<CatalogItem> catalogRepo, IRepository<Order> orderRepo, ClaimsPrincipal user) =>
            {
                var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (request.Items == null || !request.Items.Any())
                    return Results.BadRequest("Order must contain at least one item.");

                var catalogIds = request.Items.Select(i => i.CatalogItemId).ToList();
                var catalogItems = await catalogRepo.ListAsync(new CatalogItemsByIdsSpec(catalogIds));

                var orderItems = new List<OrderItem>();
                foreach (var lineItem in request.Items)
                {
                    var catalogItem = catalogItems.FirstOrDefault(c => c.Id == lineItem.CatalogItemId);
                    if (catalogItem == null)
                        return Results.BadRequest($"Catalog item {lineItem.CatalogItemId} not found.");
                    if (lineItem.Quantity <= 0)
                        return Results.BadRequest($"Quantity for item {lineItem.CatalogItemId} must be positive.");

                    var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri ?? "eCatalog-item-default.png");
                    orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, lineItem.Quantity));
                }

                var shipTo = new Address(
                    request.ShipToStreet ?? "TBD",
                    request.ShipToCity ?? "TBD",
                    request.ShipToState ?? "TBD",
                    request.ShipToCountry ?? "TBD",
                    request.ShipToZipCode ?? "00000");

                var order = new Order(buyerId, shipTo, orderItems);
                await orderRepo.AddAsync(order);

                return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse
                {
                    OrderId = order.Id,
                    Total = order.Total(),
                    Status = order.Status.ToString()
                });
            })
            .Produces<CreateOrderResponse>(201)
            .WithTags("OrderEndpoints");
    }
}
