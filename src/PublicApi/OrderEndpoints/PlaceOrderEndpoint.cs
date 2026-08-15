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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderRequest
{
    public List<PlaceOrderItem> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }

    /// <summary>Set server-side from the token; any client-supplied value is ignored.</summary>
    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class PlaceOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment and
/// reuses the app's existing Order/OrderItem model; the buyer comes from the token.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();
                request.BuyerId = buyerId;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var order = await orderPaymentService.PlaceOrderAsync(request.BuyerId!, lines, MapAddress(request.ShipToAddress));
        return Results.Created($"api/orders/{order.Id}", OrderPaymentMapper.ToDto(order));
    }

    private static Address MapAddress(ShippingAddressDto? address)
    {
        // Shipping address is optional here; default to a store-pickup placeholder the Order entity accepts.
        if (address is null)
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");

        return new Address(address.Street, address.City, address.State, address.Country, address.ZipCode);
    }
}
