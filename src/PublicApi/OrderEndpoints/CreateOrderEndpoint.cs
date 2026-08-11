using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper. The order
/// starts in the AwaitingPayment state; no money moves here.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IPaymentService service, HttpContext ctx) =>
                await HandleAsync(request, service, ctx))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentService service, HttpContext ctx)
    {
        var buyerId = PaymentMapper.GetBuyerId(ctx.User);

        if (request?.Items is null || request.Items.Count == 0)
        {
            throw new PaymentValidationException("An order must contain at least one item.");
        }

        var lines = request.Items
            .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
            .ToList();

        ShippingAddressInput? address = request.ShipToAddress is null
            ? null
            : new ShippingAddressInput(
                request.ShipToAddress.Street,
                request.ShipToAddress.City,
                request.ShipToAddress.State,
                request.ShipToAddress.Country,
                request.ShipToAddress.ZipCode);

        var orderId = await service.PlaceOrderAsync(buyerId, lines, address, ctx.RequestAborted);

        return Results.Created($"api/orders/{orderId}",
            new CreateOrderResponse(orderId, OrderStatus.AwaitingPayment.ToString()));
    }
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public AddressRequest? ShipToAddress { get; set; }
}

/// <summary>Response carrying the new order's identifier as a top-level field.</summary>
public record CreateOrderResponse(int OrderId, string Status);
