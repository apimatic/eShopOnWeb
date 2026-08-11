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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class ShippingAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = default!;
}

/// <summary>POST /api/orders — a shopper places an order from catalog items; it awaits payment.</summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
                await HandleAsync(request, service, user))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();

        var lines = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var shipTo = request.ShipToAddress is null
            ? null
            : new ShippingAddressInput(
                request.ShipToAddress.Street, request.ShipToAddress.City, request.ShipToAddress.State,
                request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var order = await service.PlaceOrderAsync(buyerId, lines, shipTo);

        var response = new PlaceOrderResponse { OrderId = order.Id, Order = order.ToDto() };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
