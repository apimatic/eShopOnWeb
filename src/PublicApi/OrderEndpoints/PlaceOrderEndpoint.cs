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

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request,
                   IRepository<Order> orderRepo,
                   IReadRepository<CatalogItem> catalogRepo,
                   ClaimsPrincipal user,
                   CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (request.Items == null || request.Items.Count == 0)
                    return Results.BadRequest(new { error = "At least one item is required." });

                var catalogItemIds = request.Items.Select(i => i.CatalogItemId).ToArray();
                var catalogSpec = new CatalogItemsSpecification(catalogItemIds);
                var catalogItems = await catalogRepo.ListAsync(catalogSpec, ct);

                var orderItems = new List<OrderItem>();
                foreach (var lineItem in request.Items)
                {
                    var catalogItem = catalogItems.FirstOrDefault(c => c.Id == lineItem.CatalogItemId);
                    if (catalogItem == null)
                        return Results.BadRequest(new { error = $"Catalog item {lineItem.CatalogItemId} not found." });

                    var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
                    orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, lineItem.Quantity));
                }

                var shipTo = request.ShippingAddress;
                var address = new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);
                var order = new Order(buyerId, address, orderItems);
                order = await orderRepo.AddAsync(order, ct);

                return Results.Created($"/api/orders/{order.Id}", new PlaceOrderResponse { OrderId = order.Id });
            })
            .Produces<PlaceOrderResponse>(201)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IRepository<Order> service)
        => throw new System.NotSupportedException();
}
