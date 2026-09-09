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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog items. The order starts awaiting payment.
/// Returns the new order's id as a top-level <c>orderId</c>.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    private readonly PayPalSettings _settings;

    public PlaceOrderEndpoint(PayPalSettings settings) => _settings = settings;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, CancellationToken ct, IOrderPaymentService service) =>
            {
                request.UserId = user.GetUserId();
                request.Cancellation = ct;
                return await HandleAsync(request, service);
            })
            .Produces<OrderPaymentDto>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService service)
    {
        var lines = (request.Items ?? new List<PlaceOrderItem>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        Address? shipTo = request.ShipToAddress?.ToAddress();

        var order = await service.PlaceOrderAsync(request.UserId, lines, shipTo, request.Cancellation);
        var dto = OrderPaymentDto.From(order, _settings.Currency);
        return Results.Created($"api/orders/{order.Id}", dto);
    }
}

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderItem> Items { get; set; } = new();

    public ShippingAddressRequest? ShipToAddress { get; set; }

    [JsonIgnore] public string UserId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken Cancellation { get; set; }
}

public class PlaceOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }

    public Address ToAddress() => new(
        Street ?? "N/A", City ?? "N/A", State ?? "N/A", Country ?? "N/A", ZipCode ?? "00000");
}
