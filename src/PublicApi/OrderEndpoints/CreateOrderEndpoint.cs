using System;
using System.Collections.Generic;
using System.Linq;
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

public class CreateOrderRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public AddressRequest ShipToAddress { get; set; } = new();
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressRequest
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Country { get; set; } = "";
    public string ZipCode { get; set; } = "";
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = "";
}

public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, HttpContext ctx,
                   IRepository<CatalogItem> catalogRepo,
                   IRepository<Order> orderRepo) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (request.Items == null || request.Items.Count == 0)
                    return Results.BadRequest(new { error = "At least one item is required." });

                var ids = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
                var catalogSpec = new CatalogItemsSpecification(ids);
                var catalogItems = await catalogRepo.ListAsync(catalogSpec);

                var orderItems = new List<OrderItem>();
                foreach (var item in request.Items)
                {
                    var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId);
                    if (catalogItem == null)
                        return Results.BadRequest(new { error = $"Catalog item {item.CatalogItemId} not found." });
                    if (item.Quantity <= 0)
                        return Results.BadRequest(new { error = $"Quantity must be positive for item {item.CatalogItemId}." });

                    var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
                    orderItems.Add(new OrderItem(ordered, catalogItem.Price, item.Quantity));
                }

                var addr = request.ShipToAddress;
                var address = new Address(addr.Street, addr.City, addr.State, addr.Country, addr.ZipCode);
                var order = new Order(buyerId, address, orderItems);

                order = await orderRepo.AddAsync(order);

                return Results.Created($"/api/orders/{order.Id}", new CreateOrderResponse
                {
                    OrderId = order.Id,
                    Total = order.Total(),
                    PaymentStatus = order.PaymentStatus.ToString()
                });
            })
            .Produces<CreateOrderResponse>(201)
            .ProducesProblem(400)
            .WithTags("OrderEndpoints");
    }
}
