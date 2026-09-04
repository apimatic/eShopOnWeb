using System;
using System.Security.Claims;
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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items. The order starts awaiting payment.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest body, ClaimsPrincipal principal, IPaymentProcessingService paymentProcessing) =>
            {
                return await HandleAsync(new PlaceOrderRequest(body, principal.Identity?.Name ?? string.Empty), paymentProcessing);
            })
            .Produces<PlaceOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IPaymentProcessingService paymentProcessing)
    {
        var response = new PlaceOrderResponse(request.CorrelationId());

        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var lines = (request.Items ?? new List<PlaceOrderLineDto>())
            .Select(i => new PlaceOrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var ship = request.ShipTo
            ?? throw new ApplicationCore.Exceptions.DomainValidationException("A shipping address (shipTo) is required.");
        var address = new Address(ship.Street, ship.City, ship.State, ship.Country, ship.ZipCode);

        var order = await paymentProcessing.PlaceOrderAsync(request.BuyerId, lines, address);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.Total = order.Total();
        response.OrderDate = order.OrderDate;
        response.Order = OrderSummaryDto.From(order);
        return Results.Created($"/api/orders/{order.Id}", response);
    }
}

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderLineDto>? Items { get; init; }
    public ShipToDto? ShipTo { get; init; }

    /// <summary>Filled from the JWT by the route handler; not bound from the body.</summary>
    public string BuyerId { get; init; } = string.Empty;

    public PlaceOrderRequest() { }

    public PlaceOrderRequest(PlaceOrderRequest source, string buyerId)
    {
        Items = source.Items;
        ShipTo = source.ShipTo;
        BuyerId = buyerId;
        _correlationId = source.CorrelationId();
    }
}

public class PlaceOrderLineDto
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public class ShipToDto
{
    public string Street { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string ZipCode { get; init; } = string.Empty;
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }
    public PlaceOrderResponse() { }

    /// <summary>Identifier of the created order.</summary>
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public OrderSummaryDto? Order { get; set; }
}
