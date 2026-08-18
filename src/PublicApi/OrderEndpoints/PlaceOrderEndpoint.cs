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

public class PlaceOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderShippingAddress
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderRequest
{
    public List<PlaceOrderItem> Items { get; set; } = new();

    /// <summary>Optional shipping address. When omitted a placeholder is used — the focus of this API is notifications.</summary>
    public PlaceOrderShippingAddress? ShipToAddress { get; set; }
}

public class PlaceOrderResponse
{
    public PlaceOrderResponse(int orderId, decimal total)
    {
        OrderId = orderId;
        Total = total;
    }

    /// <summary>The identifier of the placed order (a top-level field so the flow can be driven end to end).</summary>
    public int OrderId { get; set; }

    public decimal Total { get; set; }
}

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing Order/OrderItem model, then tells the shopper their order was placed. The buyer's identity
/// comes from the token, not the request.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPlacementService, IOrderNotificationService>
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
            (PlaceOrderRequest request, IOrderPlacementService placementService, IOrderNotificationService notificationService) =>
                await HandleAsync(request, placementService, notificationService))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPlacementService placementService, IOrderNotificationService notificationService)
    {
        var buyerId = _httpContextAccessor.GetUserName();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var ct = _httpContextAccessor.RequestAborted();

        var items = (request.Items ?? new List<PlaceOrderItem>())
            .Select(i => new OrderRequestItem(i.CatalogItemId, i.Quantity))
            .ToList();

        var address = BuildAddress(request.ShipToAddress);

        var result = await placementService.PlaceOrderAsync(buyerId, items, address, ct);
        if (!result.IsSuccess)
        {
            if (result.Status == Ardalis.Result.ResultStatus.Invalid)
            {
                return Results.BadRequest(new { errors = result.ValidationErrors.Select(e => e.ErrorMessage) });
            }

            // Unknown catalog item ids are the caller's to fix.
            return Results.BadRequest(new { errors = result.Errors });
        }

        var order = result.Value;

        // Tell the shopper their order was placed. A messaging failure never fails order placement.
        await notificationService.NotifyOrderPlacedAsync(order, ct);

        return Results.Created($"api/orders/{order.Id}", new PlaceOrderResponse(order.Id, order.Total()));
    }

    private static Address BuildAddress(PlaceOrderShippingAddress? address)
    {
        if (address is null)
        {
            return new Address("Not provided", "Not provided", "NA", "Not provided", "00000");
        }

        return new Address(
            string.IsNullOrWhiteSpace(address.Street) ? "Not provided" : address.Street,
            string.IsNullOrWhiteSpace(address.City) ? "Not provided" : address.City,
            string.IsNullOrWhiteSpace(address.State) ? "NA" : address.State,
            string.IsNullOrWhiteSpace(address.Country) ? "Not provided" : address.Country,
            string.IsNullOrWhiteSpace(address.ZipCode) ? "00000" : address.ZipCode);
    }
}
