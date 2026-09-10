using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderLine> Items { get; set; } = new();

    /// <summary>Optional shipping address; a placeholder is used if omitted (shipping isn't the focus).</summary>
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderLine
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>Top-level identifier of the created order.</summary>
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/orders — place an order from catalog items for the signed-in shopper. The order starts
/// awaiting payment. Prices come from the catalog; the caller's identity comes from the token.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext>
{
    private readonly IOrderPaymentService _orders;
    private readonly IReadRepository<Order> _orderReadRepository;
    private readonly PayPalSettings _settings;

    public CreateOrderEndpoint(IOrderPaymentService orders, IReadRepository<Order> orderReadRepository, PayPalSettings settings)
    {
        _orders = orders;
        _orderReadRepository = orderReadRepository;
        _settings = settings;
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
        var ct = http.RequestAborted;
        var buyerId = http.BuyerId();

        var lines = (request.Items ?? new List<CreateOrderLine>())
            .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
            .ToList();

        var address = request.ShipToAddress is { } a
            ? new Address(a.Street, a.City, a.State, a.Country, a.ZipCode)
            : new Address("123 Main St", "Seattle", "WA", "USA", "98101");

        var orderId = await _orders.PlaceOrderAsync(buyerId, lines, address, ct);

        var order = await _orderReadRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = orderId,
            Status = order!.Status.ToString(),
            Total = order.Total(),
            Currency = _settings.Currency
        };
        return Results.Created($"api/orders/{orderId}", response);
    }
}
