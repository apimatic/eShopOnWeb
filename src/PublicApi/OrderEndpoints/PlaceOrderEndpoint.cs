using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderLineDto> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }

    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken RequestAborted { get; set; }
}

public class PlaceOrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// Places an order for the signed-in shopper from catalog items. Prices come from the catalog; the order
/// starts awaiting payment.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderService>
{
    private readonly PaymentSettings _paymentSettings;

    public PlaceOrderEndpoint(PaymentSettings paymentSettings)
    {
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderService orderService, CancellationToken ct) =>
            {
                request.BuyerId = PaymentEndpointUser.BuyerId(user);
                request.RequestAborted = ct;
                return await HandleAsync(request, orderService);
            })
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderService orderService)
    {
        var a = request.ShipToAddress;
        if (a is null || string.IsNullOrWhiteSpace(a.Street) || string.IsNullOrWhiteSpace(a.City)
            || string.IsNullOrWhiteSpace(a.Country) || string.IsNullOrWhiteSpace(a.ZipCode))
            throw new OrderValidationException("shipToAddress with street, city, country and zipCode is required.");

        var lines = (request.Items ?? new()).Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var order = await orderService.PlaceOrderAsync(request.BuyerId, lines,
            new Address(a.Street, a.City, a.State ?? string.Empty, a.Country, a.ZipCode));

        var response = new OrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderDto.From(order, null, _paymentSettings.Currency)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

internal static class PaymentEndpointUser
{
    /// <summary>The caller's identity from the JWT — the same value orders and saved cards are owned by.</summary>
    public static string BuyerId(ClaimsPrincipal user) =>
        user.Identity?.Name is { Length: > 0 } name
            ? name
            : throw new PaymentRequestException(PaymentErrorKind.Validation, "no_identity", "The token does not identify a user.");
}
