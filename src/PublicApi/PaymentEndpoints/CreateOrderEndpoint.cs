using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreateOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public OrderDto? Order { get; set; }
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order reuses the existing
/// Order/OrderItem model and starts awaiting payment. Amounts come from catalog prices, not the caller.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
                await HandleAsync(request, user, service))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service)
    {
        var buyerId = user.GetBuyerId();
        var lines = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
            .ToList();
        var shipTo = (request.ShipToAddress ?? new AddressDto()).ToDomain();

        var orderId = await service.PlaceOrderAsync(buyerId, lines, shipTo);
        var orders = await service.GetMyOrdersAsync(buyerId);
        var order = orders.FirstOrDefault(o => o.OrderId == orderId);

        var response = new CreateOrderResponse
        {
            OrderId = orderId,
            Order = order is null ? null : OrderDto.From(order)
        };
        return Results.Created($"api/orders/{orderId}", response);
    }
}
