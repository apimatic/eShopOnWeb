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
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper and lets them know it was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext) =>
            {
                request.BuyerId = httpContext.User.Identity?.Name;
                var services = httpContext.RequestServices;
                return await HandleAsync(request,
                    services.GetRequiredService<IRepository<Order>>(),
                    services.GetRequiredService<IRepository<CatalogItem>>(),
                    services.GetRequiredService<IOrderNotificationService>());
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository, IOrderNotificationService notificationService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.BuyerId) || request.Items.Count == 0 || request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(response);
        }

        var catalogItems = await catalogItemRepository.ListAsync(new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray()));
        if (catalogItems.Count != request.Items.Select(i => i.CatalogItemId).Distinct().Count())
        {
            return Results.BadRequest(response);
        }

        var orderItems = request.Items
            .Select(i =>
            {
                var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
                return new OrderItem(new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri), catalogItem.Price, i.Quantity);
            })
            .ToList();

        var address = new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
            request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var order = await orderRepository.AddAsync(new Order(request.BuyerId, address, orderItems));

        // Best-effort: a message that cannot go out never fails the order.
        await notificationService.NotifyOrderPlacedAsync(order);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.Total = order.Total();
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();
    public ShipToAddress ShipToAddress { get; set; } = new();

    public string? BuyerId { get; set; }
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddress
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}
