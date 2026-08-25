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
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, HttpContext ctx,
                   IRepository<Order> orderRepo,
                   IRepository<CatalogItem> catalogRepo,
                   IRepository<PaymentRecord> paymentRepo,
                   PayPalSettings settings) =>
            {
                request.BuyerId = ctx.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, orderRepo, catalogRepo, paymentRepo, settings);
            })
            .Produces<PlaceOrderResponse>(201)
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IRepository<Order> repo)
        => throw new System.NotSupportedException();

    private static async Task<IResult> HandleAsync(
        PlaceOrderRequest request,
        IRepository<Order> orderRepo,
        IRepository<CatalogItem> catalogRepo,
        IRepository<PaymentRecord> paymentRepo,
        PayPalSettings settings)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();
        if (request.Items == null || request.Items.Count == 0)
            return Results.BadRequest(new { error = "Order must have at least one item." });

        var itemIds = request.Items.Select(i => i.CatalogItemId).ToArray();
        var catalogSpec = new CatalogItemsSpecification(itemIds);
        var catalogItems = await catalogRepo.ListAsync(catalogSpec);

        var orderItems = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            var cat = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (cat == null)
                return Results.BadRequest(new { error = $"Catalog item {line.CatalogItemId} not found." });
            if (line.Quantity <= 0)
                return Results.BadRequest(new { error = $"Quantity for item {line.CatalogItemId} must be > 0." });

            var itemOrdered = new CatalogItemOrdered(cat.Id, cat.Name, cat.PictureUri ?? string.Empty);
            orderItems.Add(new OrderItem(itemOrdered, cat.Price, line.Quantity));
        }

        var address = new Address("1 Main St", "Springfield", "IL", "US", "62701");
        var order = new Order(request.BuyerId, address, orderItems);
        order = await orderRepo.AddAsync(order);

        var paymentRecord = new PaymentRecord(order.Id, settings.Currency);
        await paymentRepo.AddAsync(paymentRecord);

        return Results.Created($"api/orders/{order.Id}",
            new PlaceOrderResponse { OrderId = order.Id, Total = order.Total(), Status = "AwaitingPayment" });
    }
}
