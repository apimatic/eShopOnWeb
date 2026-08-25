using System;
using System.Collections.Generic;
using System.Linq;
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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request,
                   IRepository<Order> orderRepo,
                   IRepository<CatalogItem> catalogRepo,
                   IRepository<PaymentRecord> paymentRepo,
                   HttpContext ctx,
                   CancellationToken ct) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (request.Items == null || !request.Items.Any())
                    return Results.BadRequest(new { error = "Order must contain at least one item." });

                var ids = request.Items.Select(i => i.CatalogItemId).ToArray();
                var catalogSpec = new CatalogItemsSpecification(ids);
                var catalogItems = await catalogRepo.ListAsync(catalogSpec, ct);

                var orderItems = new List<OrderItem>();
                foreach (var line in request.Items)
                {
                    var ci = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
                    if (ci == null)
                        return Results.BadRequest(new { error = $"Catalog item {line.CatalogItemId} not found." });
                    if (line.Quantity <= 0)
                        return Results.BadRequest(new { error = $"Quantity must be positive for item {line.CatalogItemId}." });

                    var itemOrdered = new CatalogItemOrdered(ci.Id, ci.Name, ci.PictureUri ?? "");
                    orderItems.Add(new OrderItem(itemOrdered, ci.Price, line.Quantity));
                }

                var address = new Address(
                    request.ShippingAddress?.Street ?? "123 Main St.",
                    request.ShippingAddress?.City ?? "Kent",
                    request.ShippingAddress?.State ?? "OH",
                    request.ShippingAddress?.Country ?? "United States",
                    request.ShippingAddress?.ZipCode ?? "44240");

                var order = new Order(buyerId, address, orderItems);
                await orderRepo.AddAsync(order, ct);

                var payment = new PaymentRecord(order.Id, buyerId);
                await paymentRepo.AddAsync(payment, ct);

                return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse { OrderId = order.Id });
            })
            .Produces<CreateOrderResponse>(201)
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> orderRepo)
        => throw new NotImplementedException();
}
