using System;
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
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PlaceOrderRequest : BaseRequest
{
    /// <summary>Set server-side from the token; not bound from the request body.</summary>
    public string? BuyerId { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }
    public PlaceOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper. Reuses the existing Order model; the
/// order starts awaiting payment. Prices come from the catalog, the buyer from the token.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request.BuyerId = PaymentRequestMapper.GetBuyerId(user);
                return await HandleAsync(request, service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService service)
    {
        var lines = request.Items.Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity));
        var address = BuildAddress(request.ShipToAddress);

        var orderId = await service.PlaceOrderAsync(request.BuyerId!, lines, address);

        return Results.Created($"api/orders/{orderId}", new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = orderId,
            Status = "AwaitingPayment"
        });
    }

    private static Address BuildAddress(ShippingAddressDto? dto) =>
        new(
            Fallback(dto?.Street),
            Fallback(dto?.City),
            dto?.State ?? string.Empty,
            Fallback(dto?.Country),
            Fallback(dto?.ZipCode));

    private static string Fallback(string? value) => string.IsNullOrWhiteSpace(value) ? "N/A" : value!;
}
