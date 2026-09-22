using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PlaceOrderItemDto
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
    public List<PlaceOrderItemDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipTo { get; set; }
}

/// <summary>POST /api/orders — places an order from catalog items for the signed-in shopper (awaiting payment).</summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public PlaceOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderPaymentService service) => await HandleAsync(request, service))
            .WithTags("PaymentOrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService service) =>
        PaymentEndpointSupport.ExecuteAsync(async () =>
        {
            var buyerId = PaymentEndpointSupport.RequireUserName(_http);
            var ct = PaymentEndpointSupport.RequestAborted(_http);

            var items = request.Items.Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity)).ToList();
            var shipTo = request.ShipTo is null
                ? null
                : new ShippingAddressInput(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State,
                    request.ShipTo.Country, request.ShipTo.ZipCode);

            var orderId = await service.PlaceOrderAsync(buyerId, items, shipTo, ct);
            return Results.Created($"api/orders/{orderId}", new { orderId });
        });
}
