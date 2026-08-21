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
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>A catalog item and quantity on a new order.</summary>
public class PlaceOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>An optional shipping address for a new order.</summary>
public class ShipToAddressPayload
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderItem> Items { get; set; } = new();
    public ShipToAddressPayload? ShipToAddress { get; set; }

    /// <summary>Set from the JWT, never from the request body.</summary>
    [JsonIgnore]
    public string CallerId { get; set; } = string.Empty;
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public PlaceOrderResponse() { }

    /// <summary>The new order's identifier (top-level, so the flow can be driven end to end).</summary>
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.CallerId = user.GetUserName();
                return await HandleAsync(request, service, ct);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService service) =>
        HandleAsync(request, service, default);

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService service, CancellationToken ct)
    {
        var command = new PlaceOrderCommand(
            request.Items.Select(i => new OrderLineCommand(i.CatalogItemId, i.Quantity)).ToList(),
            request.ShipToAddress is { } a ? new AddressCommand(a.Street, a.City, a.State, a.Country, a.ZipCode) : null);

        var orderId = await service.PlaceOrderAsync(command, request.CallerId, ct);

        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = orderId,
            Status = PaymentStatus.AwaitingPayment.ToString()
        };
        return Results.Created($"api/orders/{orderId}", response);
    }
}
