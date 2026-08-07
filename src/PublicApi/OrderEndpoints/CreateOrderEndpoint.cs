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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext>
{
    // A default ship-to address, used when the caller does not supply one. Keeps the payment flow
    // drivable with just item ids while reusing the existing order model (which requires an address).
    // A fresh instance is created per order — the address is an owned entity, so sharing one instance
    // across orders confuses EF's key tracking.
    private static Address CreateDefaultShipToAddress() =>
        new("123 Main St", "Redmond", "WA", "US", "98052");

    private readonly IOrderPaymentService _orderPaymentService;

    public CreateOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext http)
    {
        var buyerId = http.User.GetBuyerId();

        var lines = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var shipToAddress = request.ShipToAddress is null
            ? CreateDefaultShipToAddress()
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
                request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var order = await _orderPaymentService.PlaceOrderAsync(buyerId, lines, shipToAddress, http.RequestAborted);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = order.ToSummary()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
