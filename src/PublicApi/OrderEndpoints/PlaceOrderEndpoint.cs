using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;
using Microsoft.eShopWeb.PublicApi.PayPal;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IRepository<CatalogItem>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request,
                   HttpContext httpContext,
                   IReadRepository<CatalogItem> catalogRepo,
                   IRepository<Order> orderRepo,
                   IRepository<OrderPayment> paymentRepo,
                   IOptions<PayPalSettings> paypalOptions) =>
            {
                var userName = httpContext.User.Identity!.Name!;
                var settings = paypalOptions.Value;

                if (request.Items == null || request.Items.Count == 0)
                    return Results.BadRequest(new { error = "At least one item is required." });

                var orderItems = new List<OrderItem>();
                foreach (var item in request.Items)
                {
                    var catalogItem = await catalogRepo.GetByIdAsync(item.CatalogItemId);
                    if (catalogItem == null)
                        return Results.BadRequest(new { error = $"Catalog item {item.CatalogItemId} not found." });
                    if (item.Quantity <= 0)
                        return Results.BadRequest(new { error = $"Quantity for item {item.CatalogItemId} must be positive." });

                    var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri ?? string.Empty);
                    orderItems.Add(new OrderItem(ordered, catalogItem.Price, item.Quantity));
                }

                var shipTo = new Address(
                    request.Street ?? "Unknown",
                    request.City ?? "Unknown",
                    request.State ?? string.Empty,
                    request.Country ?? "US",
                    request.ZipCode ?? "00000"
                );

                var order = new Order(userName, shipTo, orderItems);
                order = await orderRepo.AddAsync(order);

                var currency = string.IsNullOrWhiteSpace(settings.Currency) ? "USD" : settings.Currency;
                var payment = new OrderPayment(order.Id, userName, order.Total(), currency);
                await paymentRepo.AddAsync(payment);

                return Results.Created($"api/orders/{order.Id}", new PlaceOrderResponse(order.Id));
            })
            .Produces<PlaceOrderResponse>(201)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IRepository<CatalogItem> service)
        => Task.FromResult(Results.StatusCode(501));
}

public record PlaceOrderItemRequest(int CatalogItemId, int Quantity);

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderItemRequest>? Items { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public record PlaceOrderResponse(int OrderId);
