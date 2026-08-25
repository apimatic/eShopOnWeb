using System.Security.Claims;
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
            async (CreateOrderRequest request, HttpContext ctx,
                   IRepository<Order> orderRepo,
                   IRepository<CatalogItem> catalogRepo) =>
            {
                var username = ctx.User.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(username))
                    return Results.Unauthorized();

                if (request.Items == null || request.Items.Count == 0)
                    return Results.BadRequest("At least one item is required.");

                var catalogIds = request.Items.Select(i => i.CatalogItemId).ToArray();
                var catalogSpec = new CatalogItemsSpecification(catalogIds);
                var catalogItems = await catalogRepo.ListAsync(catalogSpec);

                var orderItems = new List<OrderItem>();
                foreach (var lineItem in request.Items)
                {
                    var catalogItem = catalogItems.FirstOrDefault(c => c.Id == lineItem.CatalogItemId);
                    if (catalogItem == null)
                        return Results.BadRequest($"Catalog item {lineItem.CatalogItemId} not found.");
                    if (lineItem.Quantity <= 0)
                        return Results.BadRequest($"Quantity for item {lineItem.CatalogItemId} must be > 0.");

                    var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
                    orderItems.Add(new OrderItem(ordered, catalogItem.Price, lineItem.Quantity));
                }

                var address = new Address(
                    request.ShippingAddress?.Street ?? "",
                    request.ShippingAddress?.City ?? "",
                    request.ShippingAddress?.State ?? "",
                    request.ShippingAddress?.Country ?? "",
                    request.ShippingAddress?.ZipCode ?? "");

                var order = new Order(username, address, orderItems);
                order = await orderRepo.AddAsync(order);

                return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse
                {
                    OrderId = order.Id,
                    Status = "PendingPayment",
                    Total = order.Total(),
                    OrderDate = order.OrderDate
                });
            })
            .Produces<CreateOrderResponse>(201)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> repository)
        => throw new NotImplementedException();
}

public class CreateOrderRequest
{
    public List<OrderLineItem>? Items { get; set; }
    public ShippingAddressDto? ShippingAddress { get; set; }
}

public class OrderLineItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
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
    public string Status { get; set; } = "";
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
}
