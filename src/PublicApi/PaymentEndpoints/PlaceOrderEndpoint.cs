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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderItem> Items { get; set; } = new();
    public ShipToAddressDto? ShipToAddress { get; set; }
}

public class PlaceOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper. The order
/// starts awaiting payment. Returns the new orderId as a top-level field.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, ClaimsPrincipal, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
                await HandleAsync(request, user, paymentService))
            .Produces<OrderPaymentDto>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, ClaimsPrincipal user,
        IPaymentService paymentService)
    {
        var buyerId = CallerId.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "An order must contain at least one item." });
        }

        var lines = request.Items
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var a = request.ShipToAddress;
        var shipTo = a is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "N/A")
            : new Address(a.Street, a.City, a.State, a.Country, a.ZipCode);

        var order = await paymentService.PlaceOrderAsync(buyerId, lines, shipTo);
        var dto = PaymentMapping.ToDto(new OrderPaymentView(order, null));

        return Results.Created($"api/orders/{order.Id}", dto);
    }
}
