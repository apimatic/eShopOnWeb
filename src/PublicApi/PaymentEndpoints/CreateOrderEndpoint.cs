using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
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

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }

    [JsonIgnore]
    public string CallerId { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting
/// payment. Returns the created order's id as a top-level field.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
            {
                request.CallerId = PaymentEndpointHelpers.GetCallerId(user);
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
    {
        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var address = request.ShipToAddress is { } a
            ? new Address(a.Street, a.City, a.State, a.Country, a.ZipCode)
            : new Address("N/A", "N/A", "N/A", "N/A", "00000");

        var order = await service.PlaceOrderAsync(request.CallerId, lines, address);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderDto.From(order)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
