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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Places an order from catalog items for the signed-in shopper and tells them it was placed.</summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderNotificationService service, CancellationToken ct) =>
            {
                request.BuyerId = CallerIdentity.GetBuyerId(user);
                return await HandleAsync(request, service, ct);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service)
        => HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();
        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest(new { message = "An order must have at least one item." });

        var lines = request.Items
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        Address? address = request.ShipToAddress is { } a
            ? new Address(a.Street ?? "N/A", a.City ?? "N/A", a.State ?? "N/A", a.Country ?? "N/A", a.ZipCode ?? "00000")
            : null;

        var orderId = await service.PlaceOrderAsync(request.BuyerId, lines, address, ct);

        return Results.Created($"api/orders/{orderId}", new PlaceOrderResponse
        {
            OrderId = orderId,
            Status = OrderStatus.Placed.ToString()
        });
    }
}

public class PlaceOrderRequest
{
    public List<OrderLineDto>? Items { get; set; }
    public AddressDto? ShipToAddress { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? BuyerId { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
