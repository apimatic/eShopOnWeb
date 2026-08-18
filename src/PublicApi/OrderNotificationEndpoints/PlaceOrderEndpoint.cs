using System;
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

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();

    /// <summary>Optional; a placeholder shipping address is used when omitted.</summary>
    public ShippingAddressDto? ShipToAddress { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }
    public PlaceOrderResponse() { }

    public int OrderId { get; set; }
}

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing order/order-item model. The shopper is told their order was placed; a message that cannot
/// be sent never fails the placement.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service)
    {
        var lines = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var address = BuildAddress(request.ShipToAddress);

        var result = await service.PlaceOrderAsync(request.BuyerId, lines, address);
        if (!result.Success || result.OrderId is null)
            return Results.BadRequest(new { error = result.Error ?? "The order could not be placed." });

        var response = new PlaceOrderResponse(request.CorrelationId()) { OrderId = result.OrderId.Value };
        return Results.Created($"api/orders/{response.OrderId}", response);
    }

    private static Address BuildAddress(ShippingAddressDto? dto)
    {
        if (dto is null)
            return new Address("Not specified", "Not specified", "Not specified", "Not specified", "00000");

        return new Address(
            string.IsNullOrWhiteSpace(dto.Street) ? "Not specified" : dto.Street,
            string.IsNullOrWhiteSpace(dto.City) ? "Not specified" : dto.City,
            dto.State ?? string.Empty,
            string.IsNullOrWhiteSpace(dto.Country) ? "Not specified" : dto.Country,
            string.IsNullOrWhiteSpace(dto.ZipCode) ? "00000" : dto.ZipCode);
    }
}
