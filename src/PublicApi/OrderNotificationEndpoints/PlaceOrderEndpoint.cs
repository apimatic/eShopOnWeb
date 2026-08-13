using System;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class OrderLineInput
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressInput
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderRequest : BaseRequest
{
    public List<OrderLineInput> Items { get; set; } = new();

    /// <summary>Optional. A placeholder shipping address is used when none is supplied.</summary>
    public ShipToAddressInput? ShipToAddress { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }
    public PlaceOrderResponse() { }

    /// <summary>Identifier of the created order.</summary>
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

/// <summary>
/// Places an order for the signed-in shopper from catalog items, reusing the app's existing
/// order/order-item model. The shopper is told their order was placed.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, INotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PlaceOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, INotificationService service) =>
                await HandleAsync(request, service))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, INotificationService service)
    {
        var ownerId = _httpContextAccessor.HttpContext!.User.GetUserId();

        var lines = (request.Items ?? new List<OrderLineInput>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var address = BuildAddress(request.ShipToAddress);

        var order = await service.PlaceOrderAsync(ownerId, lines, address);

        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address BuildAddress(ShipToAddressInput? input)
    {
        if (input is null)
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");

        return new Address(
            string.IsNullOrWhiteSpace(input.Street) ? "N/A" : input.Street,
            string.IsNullOrWhiteSpace(input.City) ? "N/A" : input.City,
            input.State ?? string.Empty,
            string.IsNullOrWhiteSpace(input.Country) ? "N/A" : input.Country,
            string.IsNullOrWhiteSpace(input.ZipCode) ? "00000" : input.ZipCode);
    }
}
