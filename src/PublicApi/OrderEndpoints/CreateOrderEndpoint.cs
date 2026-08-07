using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequestDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }

    /// <summary>Set from the caller's token, not the request body.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>The identifier of the created order (top-level, so callers can drive the flow).</summary>
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts
/// awaiting payment. Reuses the app's existing Order/OrderItem model.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderService orderService, CancellationToken ct) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, orderService, ct);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService)
        => HandleAsync(request, orderService, default);

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService, CancellationToken ct)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        var address = request.ShipToAddress is { } a
            ? new Address(a.Street, a.City, a.State, a.Country, a.ZipCode)
            : new Address("123 Main St.", "Kent", "OH", "United States", "44240");

        var items = request.Items.Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity));

        var order = await orderService.CreateOrderAsync(request.BuyerId, items, address);

        response.OrderId = order.Id;
        response.Order = OrderDto.From(order);
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
