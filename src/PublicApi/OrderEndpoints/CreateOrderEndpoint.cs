using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderService>
{
    private readonly IOrderNotificationService _notifications;

    public CreateOrderEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, HttpContext httpContext, IOrderService orders) =>
            {
                request.BuyerId = httpContext.User.GetRequiredBuyerId();
                return await HandleAsync(request, orders);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orders)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "At least one catalog item is required." });
        }

        if (request.Items.Any(item => item.CatalogItemId <= 0 || item.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "Each item needs a catalogItemId and a quantity greater than zero." });
        }

        var address = request.ShipTo is null
            ? new Address("123 Main St.", "Kent", "OH", "United States", "44240")
            : new Address(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

        var lines = request.Items.Select(item => new CatalogOrderLine(item.CatalogItemId, item.Quantity)).ToList();

        Order order;
        try
        {
            order = await orders.CreateOrderFromCatalogAsync(request.BuyerId, lines, address);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }

        await _notifications.NotifyOrderPlacedAsync(order);

        return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        });
    }
}

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public CreateOrderAddressRequest? ShipTo { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
