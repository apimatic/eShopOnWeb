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
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PlaceOrderRequest : BaseRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = "AwaitingPayment";
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
}

/// <summary>Places an order from catalog items for the signed-in shopper (awaiting payment).</summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _http;
    private readonly string _currency;

    public PlaceOrderEndpoint(IHttpContextAccessor http, IOptions<PayPalSettings> settings)
    {
        _http = http;
        _currency = settings.Value.Currency;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IPaymentService paymentService) =>
                await HandleAsync(request, paymentService))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IPaymentService paymentService)
    {
        var ctx = _http.HttpContext!;
        var buyerId = ctx.User.GetBuyerId();
        var ct = ctx.RequestAborted;

        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();

        Address? shipTo = null;
        if (request.ShipToAddress is not null)
        {
            var a = request.ShipToAddress;
            shipTo = new Address(a.Street ?? "N/A", a.City ?? "N/A", a.State ?? "N/A", a.Country ?? "N/A", a.ZipCode ?? "00000");
        }

        var order = await paymentService.PlaceOrderAsync(buyerId, lines, shipTo, ct);

        var response = new PlaceOrderResponse()
        {
            OrderId = order.Id,
            Total = order.Total(),
            Currency = _currency
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
