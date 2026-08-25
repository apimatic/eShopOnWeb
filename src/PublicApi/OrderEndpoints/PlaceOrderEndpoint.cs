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
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderRequest
{
    public List<OrderLineItem> Items { get; set; } = new();
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class OrderLineItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, HttpContext ctx) =>
            {
                return await HandleAsync(request, ctx);
            })
            .Produces<PlaceOrderResponse>(201)
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, HttpContext ctx)
    {
        var buyerId = ctx.User.FindFirstValue(ClaimTypes.Name)!;
        var sp = ctx.RequestServices;
        var catalogRepo = sp.GetRequiredService<IReadRepository<CatalogItem>>();
        var orderRepo = sp.GetRequiredService<IRepository<Order>>();
        var paymentRepo = sp.GetRequiredService<IRepository<Payment>>();
        var config = sp.GetRequiredService<IConfiguration>();
        var currency = config["PayPal:Currency"] ?? "USD";

        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest("At least one item is required.");

        var catalogIds = request.Items.Select(i => i.CatalogItemId).ToArray();
        var catalogSpec = new CatalogItemsSpecification(catalogIds);
        var catalogItems = await catalogRepo.ListAsync(catalogSpec);

        var orderItems = new List<OrderItem>();
        foreach (var lineItem in request.Items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == lineItem.CatalogItemId);
            if (catalogItem is null)
                return Results.BadRequest($"Catalog item {lineItem.CatalogItemId} not found.");
            if (lineItem.Quantity <= 0)
                return Results.BadRequest($"Quantity for item {lineItem.CatalogItemId} must be positive.");

            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri ?? string.Empty);
            orderItems.Add(new OrderItem(ordered, catalogItem.Price, lineItem.Quantity));
        }

        var address = new Address(request.Street, request.City, request.State, request.Country, request.ZipCode);
        var order = new Order(buyerId, address, orderItems);
        order = await orderRepo.AddAsync(order);

        var total = order.Total();
        var payment = new Payment(order.Id, buyerId, total, currency);
        await paymentRepo.AddAsync(payment);

        return Results.Created($"/api/orders/{order.Id}", new PlaceOrderResponse
        {
            OrderId = order.Id,
            Total = total,
            Currency = currency,
            Status = PaymentStatus.PendingPayment.ToString()
        });
    }
}
