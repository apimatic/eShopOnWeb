using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.PublicApi.Configuration;
using MinimalApi.Endpoint;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>Top-level identifier of the created order.</summary>
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities (reusing the existing
/// order model), then tells the shopper their order was placed. A messaging failure never fails the order.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPlacementService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderPlacementService placementService) =>
                await HandleAsync(request, placementService))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPlacementService placementService)
    {
        var ownerId = _httpContextAccessor.GetOwnerId();
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var ct = _httpContextAccessor.RequestAborted();

        var lines = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        Address? address = request.ShipToAddress is null
            ? null
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City, request.ShipToAddress.State,
                request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var result = await placementService.PlaceOrderAsync(ownerId, lines, address, ct);
        if (!result.IsSuccess)
        {
            return result.ToStatusResult();
        }

        var order = result.Value;

        // Tell the shopper their order was placed. Best-effort: a messaging failure never fails the order.
        var notificationService = _httpContextAccessor.RequestService<IOrderNotificationService>();
        await notificationService.NotifyOrderPlacedAsync(order, ct);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
