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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PlaceOrderRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = "AwaitingPayment";
}

/// <summary>
/// Places an order for the signed-in shopper from catalog items. Amounts come from catalog prices;
/// the order starts awaiting payment. Returns the new orderId.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
            {
                request.BuyerId = user.BuyerId();
                return await HandleAsync(request, service, ct);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService service, CancellationToken ct)
    {
        var lines = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        Address? address = request.ShipToAddress is { } a
            ? new Address(a.Street, a.City, a.State, a.Country, a.ZipCode)
            : null;

        var orderId = await service.PlaceOrderAsync(request.BuyerId, lines, address, ct);

        var orders = await service.GetMyOrdersAsync(request.BuyerId, ct);
        var placed = orders.First(o => o.OrderId == orderId);

        var response = new PlaceOrderResponse
        {
            OrderId = orderId,
            Total = placed.Total,
            PaymentStatus = placed.PaymentStatus
        };
        return Results.Created($"api/orders/{orderId}", response);
    }
}
