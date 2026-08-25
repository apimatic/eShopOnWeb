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
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
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
                   IRepository<Order> orderRepository,
                   IRepository<CatalogItem> catalogRepository,
                   IRepository<OrderPayment> paymentRepository,
                   HttpContext ctx) =>
            {
                var buyer = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyer))
                    return Results.Unauthorized();

                if (request.Items == null || request.Items.Count == 0)
                    return Results.BadRequest(new { error = "At least one item is required." });

                var catalogIds = request.Items.Select(i => i.CatalogItemId).ToArray();
                var catalogSpec = new CatalogItemsSpecification(catalogIds);
                var catalogItems = await catalogRepository.ListAsync(catalogSpec);

                var orderItems = new List<OrderItem>();
                foreach (var item in request.Items)
                {
                    var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId);
                    if (catalogItem == null)
                        return Results.BadRequest(new { error = $"Catalog item {item.CatalogItemId} not found." });
                    if (item.Quantity <= 0)
                        return Results.BadRequest(new { error = $"Quantity for item {item.CatalogItemId} must be > 0." });

                    var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
                    orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
                }

                var address = request.ShippingAddress != null
                    ? new Address(
                        request.ShippingAddress.Street,
                        request.ShippingAddress.City,
                        request.ShippingAddress.State,
                        request.ShippingAddress.Country,
                        request.ShippingAddress.ZipCode)
                    : new Address("N/A", "N/A", "N/A", "US", "00000");

                var order = new Order(buyer, address, orderItems);
                order = await orderRepository.AddAsync(order);

                var payment = new OrderPayment(order.Id);
                await paymentRepository.AddAsync(payment);

                var response = new CreateOrderResponse(request.CorrelationId())
                {
                    OrderId = order.Id,
                    Total = order.Total(),
                    Currency = "USD"
                };
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(201)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> repository)
        => Task.FromResult(Results.StatusCode(501) as IResult);
}
