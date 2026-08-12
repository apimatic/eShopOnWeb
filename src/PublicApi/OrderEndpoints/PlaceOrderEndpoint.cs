using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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

public class PlaceOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShipToAddressDto? ShipToAddress { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
}

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the
/// app's existing order model. The shopper is told their order was placed (best-effort).
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    private readonly IOrderNotificationService _orderNotificationService;

    public PlaceOrderEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, CancellationToken ct) =>
                await HandleAsync(request, user, ct))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.GetUsername();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var lines = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        Address? address = request.ShipToAddress is null
            ? null
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City, request.ShipToAddress.State,
                request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var orderId = await _orderNotificationService.PlaceOrderAsync(buyerId, lines, address, ct);

        var response = new PlaceOrderResponse { OrderId = orderId };
        return Results.Created($"api/orders/{orderId}", response);
    }
}
