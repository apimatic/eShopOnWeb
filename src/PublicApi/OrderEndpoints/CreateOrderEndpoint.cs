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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>, IRepository<CatalogItem>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IRepository<Order> orderRepo, IRepository<CatalogItem> catalogRepo, HttpContext ctx, CancellationToken ct) =>
            {
                request.BuyerId = ctx.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, orderRepo, catalogRepo);
            })
            .Produces<CreateOrderResponse>(201)
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> orderRepo, IRepository<CatalogItem> catalogRepo)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        if (request.Items == null || request.Items.Count == 0)
            return Results.BadRequest("Order must contain at least one item.");

        var orderItems = new List<OrderItem>();
        foreach (var item in request.Items)
        {
            var catalogItem = await catalogRepo.GetByIdAsync(item.CatalogItemId);
            if (catalogItem == null)
                return Results.BadRequest($"Catalog item {item.CatalogItemId} not found.");
            if (item.Quantity <= 0)
                return Results.BadRequest($"Quantity for item {item.CatalogItemId} must be positive.");

            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            orderItems.Add(new OrderItem(ordered, catalogItem.Price, item.Quantity));
        }

        var address = new Address(
            request.ShipToStreet ?? "N/A",
            request.ShipToCity ?? "N/A",
            request.ShipToState ?? "N/A",
            request.ShipToCountry ?? "US",
            request.ShipToZipCode ?? "00000");

        var order = new Order(request.BuyerId, address, orderItems);
        order = await orderRepo.AddAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Total = order.Total(),
            Status = order.PaymentStatus.ToString()
        };

        return Results.Created($"/api/orders/{order.Id}", response);
    }
}

public class CreateOrderRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public List<OrderItemRequest>? Items { get; set; }
    public string? ShipToStreet { get; set; }
    public string? ShipToCity { get; set; }
    public string? ShipToState { get; set; }
    public string? ShipToCountry { get; set; }
    public string? ShipToZipCode { get; set; }
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
}
