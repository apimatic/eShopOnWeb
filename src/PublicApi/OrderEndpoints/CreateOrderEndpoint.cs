using System;
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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    /// <summary>Catalog items and quantities to order.</summary>
    public List<OrderLineDto> Items { get; set; } = new();

    /// <summary>Optional shipping address.</summary>
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>Top-level identifier of the created order.</summary>
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// POST /api/orders — place an order from catalog items for the signed-in shopper. The order
/// starts awaiting payment. Reuses the app's existing order/order-item model.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
                await HandleAsync(request, user, service))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    private static async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service)
    {
        var buyerId = user.GetBuyerId();
        if (buyerId is null) return Results.Unauthorized();

        try
        {
            var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
            var order = await service.CreateOrderAsync(buyerId, lines, request.ShipToAddress?.ToAddress());
            var response = new CreateOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Order = OrderDto.From(order)
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (Exception ex) when (PaymentErrorMapper.TryMap(ex, out var result))
        {
            return result;
        }
    }
}
