using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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

public class OrderLineModel
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressModel
{
    public string Street { get; set; } = "N/A";
    public string City { get; set; } = "N/A";
    public string State { get; set; } = "N/A";
    public string Country { get; set; } = "N/A";
    public string ZipCode { get; set; } = "00000";
}

public class PlaceOrderRequest
{
    public List<OrderLineModel> Items { get; set; } = new();
    public ShippingAddressModel? ShipToAddress { get; set; }
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "AwaitingPayment";
    public int ItemCount { get; set; }
}

/// <summary>
/// POST /api/orders — places an order (awaiting payment) from catalog items for the signed-in shopper.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user) =>
                await HandleAsync(request, paymentService, user))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IPaymentService paymentService, ClaimsPrincipal user)
    {
        var buyerId = CallerIdentity.BuyerId(user);

        var lines = (request.Items ?? new List<OrderLineModel>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        Address? shipTo = request.ShipToAddress is null
            ? null
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City, request.ShipToAddress.State,
                request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var order = await paymentService.PlaceOrderAsync(buyerId, lines, shipTo);

        var response = new PlaceOrderResponse
        {
            OrderId = order.Id,
            Total = order.Total(),
            ItemCount = order.OrderItems.Sum(oi => oi.Units)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
