using System;
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
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing order/order-item model. The shopper is told their order was placed.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderNotificationService service, CancellationToken ct) =>
            {
                return await HandleAsync(request, user, service, ct);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, ClaimsPrincipal user,
        IOrderNotificationService service, CancellationToken ct)
    {
        var buyerId = user.GetUserId();
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var lines = (request.Items ?? new List<OrderLineRequest>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        Address? shipTo = request.ShipToAddress is null
            ? null
            : new Address(
                Fallback(request.ShipToAddress.Street),
                Fallback(request.ShipToAddress.City),
                Fallback(request.ShipToAddress.State),
                Fallback(request.ShipToAddress.Country),
                Fallback(request.ShipToAddress.ZipCode));

        var order = await service.PlaceOrderAsync(buyerId, lines, shipTo, ct);

        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Total = order.Total(),
            ItemCount = order.OrderItems.Sum(i => i.Units)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static string Fallback(string? value) => string.IsNullOrWhiteSpace(value) ? "N/A" : value;
}

public class PlaceOrderRequest : BaseRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public AddressRequest? ShipToAddress { get; set; }
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }

    public PlaceOrderResponse() { }

    /// <summary>The identifier of the order just placed.</summary>
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
}
