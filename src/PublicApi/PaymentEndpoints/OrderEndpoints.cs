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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper, reusing the
/// existing Order/OrderItem model. The order starts awaiting payment.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderEndpoint.Args, IOrderPaymentService>
{
    public record Args(string? BuyerId, PlaceOrderRequest Body);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, HttpContext http, IOrderPaymentService service) =>
                await HandleAsync(new Args(http.User.GetBuyerId(), request), service))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(Args args, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(args.BuyerId))
            return Results.Unauthorized();

        var lines = (args.Body.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var a = args.Body.ShipToAddress;
        var address = a is null
            ? new Address("N/A", "N/A", "N/A", "US", "00000")
            : new Address(a.Street, a.City, a.State, a.Country, a.ZipCode);

        var orderId = await service.PlaceOrderAsync(args.BuyerId, lines, address);

        return Results.Created($"api/orders/{orderId}", new PlaceOrderResponse
        {
            OrderId = orderId,
            Status = "AwaitingPayment"
        });
    }
}

/// <summary>GET /api/my-orders — the caller's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, string?, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IOrderPaymentService service) =>
                await HandleAsync(http.User.GetBuyerId(), service))
            .Produces<List<OrderSummaryDto>>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(string? buyerId, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var orders = await service.GetMyOrdersAsync(buyerId);
        var summaries = orders
            .Select(o => new OrderSummaryDto(
                o.Order.Id,
                o.Order.OrderDate,
                o.Order.Total(),
                o.Payment is null ? null : PaymentStateDto.From(o.Payment)))
            .ToList();

        return Results.Ok(summaries);
    }
}
