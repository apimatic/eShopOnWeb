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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog items. The order is priced from the catalog
/// and starts life awaiting payment. Returns the new order id as a top-level field.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IPaymentService _paymentService;

    public CreateOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user) => await HandleAsync(request, user))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user)
    {
        var response = new CreateOrderResponse(request.CorrelationId());
        var buyerId = CallerIdentity.GetBuyerId(user);

        var lines = (request.Items ?? new List<CreateOrderItem>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var shipToAddress = request.ShipToAddress?.ToAddress() ?? DefaultAddress();

        var order = await _paymentService.PlaceOrderAsync(buyerId, lines, shipToAddress);

        response.OrderId = order.Id;
        response.Order = OrderDto.From(order);
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address DefaultAddress() =>
        new("N/A", "N/A", "N/A", "N/A", "00000");
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItem>? Items { get; set; }
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderItem
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
        Street ?? "N/A",
        City ?? "N/A",
        State ?? "N/A",
        Country ?? "N/A",
        ZipCode ?? "00000");
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>Identifier of the newly created order.</summary>
    public int OrderId { get; set; }
    public OrderDto? Order { get; set; }
}
