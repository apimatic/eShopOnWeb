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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Units { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
}

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing order/order-item model. The shopper is told their order was placed.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, ISmsNotificationService service) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, ISmsNotificationService service)
    {
        var lines = (request.Items ?? new List<OrderItemRequest>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Units))
            .ToList();

        var address = ToAddress(request.ShipToAddress);

        try
        {
            var order = await service.PlaceOrderAsync(request.BuyerId, lines, address);
            var response = new PlaceOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Total = order.Total(),
                ItemCount = order.OrderItems.Count
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static Address ToAddress(ShippingAddressRequest? a)
    {
        if (a is null)
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");

        return new Address(
            string.IsNullOrWhiteSpace(a.Street) ? "N/A" : a.Street,
            string.IsNullOrWhiteSpace(a.City) ? "N/A" : a.City,
            string.IsNullOrWhiteSpace(a.State) ? "N/A" : a.State,
            string.IsNullOrWhiteSpace(a.Country) ? "N/A" : a.Country,
            string.IsNullOrWhiteSpace(a.ZipCode) ? "00000" : a.ZipCode);
    }
}
