using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

public class PlaceOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }

    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken Ct { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }
    public PlaceOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment;
/// no money is held or moved until <c>POST /api/orders/{id}/pay</c>.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, HttpContext http, IPaymentService service, CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.CurrentBuyerId(http);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                request.BuyerId = buyerId;
                request.Ct = ct;
                return await HandleAsync(request, service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PayPalOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IPaymentService service)
    {
        var lines = request.Items
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var shipTo = request.ShipToAddress is null
            ? null
            : new ShippingAddressRequest(request.ShipToAddress.Street, request.ShipToAddress.City,
                request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var payment = await service.PlaceOrderAsync(request.BuyerId, lines, shipTo, request.Ct);

        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = payment.OrderId,
            Status = payment.Status.ToString(),
            Amount = payment.Amount,
            Currency = payment.Currency
        };
        return Results.Created($"api/orders/{payment.OrderId}", response);
    }
}
