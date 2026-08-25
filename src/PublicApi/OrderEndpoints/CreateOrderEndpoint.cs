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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request,
                   IRepository<Order> orderRepository,
                   IRepository<CatalogItem> catalogRepository,
                   ClaimsPrincipal user,
                   CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var catalogIds = request.Items.Select(i => i.CatalogItemId).ToArray();
                var catalogSpec = new CatalogItemsSpecification(catalogIds);
                var catalogItems = await catalogRepository.ListAsync(catalogSpec, ct);

                var orderItems = new List<OrderItem>();
                foreach (var item in request.Items)
                {
                    var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId);
                    if (catalogItem == null)
                        return Results.BadRequest($"Catalog item {item.CatalogItemId} not found.");
                    if (item.Quantity <= 0)
                        return Results.BadRequest($"Quantity for item {item.CatalogItemId} must be positive.");

                    var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
                    orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
                }

                if (!orderItems.Any())
                    return Results.BadRequest("Order must contain at least one item.");

                var address = new Address(
                    request.ShipToAddress.Street,
                    request.ShipToAddress.City,
                    request.ShipToAddress.State,
                    request.ShipToAddress.Country,
                    request.ShipToAddress.ZipCode);

                var order = new Order(buyerId, address, orderItems);
                order = await orderRepository.AddAsync(order, ct);

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

    public Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> dep)
        => Task.FromResult(Results.StatusCode(501));
}

public class CreateOrderRequest : BaseRequest
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
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse() : base(System.Guid.NewGuid()) { }
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
}
