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

public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest req,
                   IRepository<Order> orderRepo,
                   IReadRepository<CatalogItem> catalogRepo,
                   ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (req.Items == null || req.Items.Count == 0)
                    return Results.BadRequest(new { error = "At least one item is required." });

                var catalogItemIds = req.Items.Select(i => i.CatalogItemId).ToList();
                var catalogItems = await catalogRepo.ListAsync(new CatalogItemsByIdsSpec(catalogItemIds));

                var orderItems = new List<OrderItem>();
                foreach (var line in req.Items)
                {
                    var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
                    if (catalogItem == null)
                        return Results.BadRequest(new { error = $"Catalog item {line.CatalogItemId} not found." });
                    if (line.Quantity <= 0)
                        return Results.BadRequest(new { error = $"Quantity must be positive for item {line.CatalogItemId}." });

                    orderItems.Add(new OrderItem(
                        new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                        catalogItem.Price,
                        line.Quantity));
                }

                var address = req.ShippingAddress != null
                    ? new Address(req.ShippingAddress.Street, req.ShippingAddress.City,
                        req.ShippingAddress.State, req.ShippingAddress.Country, req.ShippingAddress.ZipCode)
                    : new Address("TBD", "TBD", "TBD", "US", "00000");

                var order = new Order(buyerId, address, orderItems);
                await orderRepo.AddAsync(order);

                return Results.Created($"/api/orders/{order.Id}", new { orderId = order.Id });
            })
            .Produces(201)
            .WithTags("OrderEndpoints");
    }
}

public record PlaceOrderRequest(List<OrderLineItem>? Items, ShippingAddressDto? ShippingAddress);
public record OrderLineItem(int CatalogItemId, int Quantity);
public record ShippingAddressDto(string Street, string City, string State, string Country, string ZipCode);
