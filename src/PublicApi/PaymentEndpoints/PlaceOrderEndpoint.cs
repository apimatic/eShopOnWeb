using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints.Models;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Places an order from catalog items for the signed-in shopper, awaiting payment.</summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PlaceOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, IOrderPaymentService orderPaymentService) =>
                await HandleAsync(request, orderPaymentService))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request?.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "At least one order item is required." });
        }

        var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity));
        var shipTo = request.ShipToAddress?.ToAddress();

        var order = await orderPaymentService.PlaceOrderAsync(buyerId, lines, shipTo);

        var response = new PlaceOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDto.FromOrder(order)
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}

/// <summary>Request body for placing an order.</summary>
public class PlaceOrderRequest
{
    /// <summary>Catalog items and quantities to order. Prices are taken from the catalog, not the caller.</summary>
    public List<OrderLineModel> Items { get; set; } = new();

    /// <summary>Optional shipping address. A placeholder is used when omitted.</summary>
    public ShippingAddressModel? ShipToAddress { get; set; }
}

public class OrderLineModel
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressModel
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToAddress() => new Address(Street, City, State, Country, ZipCode);
}

/// <summary>Response for a placed order. <see cref="OrderId"/> is the top-level identifier.</summary>
public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}
