using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PlaceOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }
    public PlaceOrderResponse() { }

    /// <summary>Identifier of the newly placed order (top-level, so the flow can be driven end to end).</summary>
    public int OrderId { get; set; }
}

/// <summary>
/// POST /api/orders — place an order from catalog items. Shopper-scoped: the buyer is the caller. The order
/// starts awaiting payment.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderCheckoutService, CancellationToken>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PlaceOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderCheckoutService checkoutService, CancellationToken cancellationToken) =>
                await HandleAsync(request, checkoutService, cancellationToken))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderCheckoutService checkoutService,
        CancellationToken cancellationToken)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        var lines = (request.Items ?? new List<OrderItemRequest>())
            .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
            .ToList();

        Address? shipTo = request.ShipToAddress is null
            ? null
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City, request.ShipToAddress.State,
                request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var orderId = await checkoutService.PlaceOrderAsync(buyerId, lines, shipTo, cancellationToken);

        var response = new PlaceOrderResponse(request.CorrelationId()) { OrderId = orderId };
        return Results.Created($"api/orders/{orderId}", response);
    }
}
