using System.Collections.Generic;
using System.Linq;
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

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications.OrderEndpoints;

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();

    /// <summary>Optional shipping address; a placeholder is used when omitted (notifications are the focus).</summary>
    public CreateOrderAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(System.Guid correlationId) : base(correlationId) { }

    /// <summary>Identifier of the placed order (top-level so the flow can be driven end to end).</summary>
    public int OrderId { get; set; }

    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Places an order from catalog item ids and quantities for the signed-in shopper, reusing the
/// app's existing order model, and tells the shopper their order was placed. The identity comes
/// from the token; a failed notification never fails the order.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                IOrderPlacementService placementService,
                IOrderNotificationService notificationService,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var lines = (request.Items ?? new List<CreateOrderItemRequest>())
                    .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
                    .ToList();

                var address = ToAddress(request.ShipToAddress);

                // May throw InvalidOrderRequestException (empty/invalid lines) -> 400 via middleware.
                var order = await placementService.PlaceOrderAsync(buyerId, lines, address, cancellationToken);

                // Best-effort: never throws, never fails the order.
                await notificationService.NotifyOrderPlacedAsync(order, cancellationToken);

                var response = new CreateOrderResponse(request.CorrelationId())
                {
                    OrderId = order.Id,
                    Total = order.Total(),
                    Status = order.Status.ToString()
                };
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    private static Address ToAddress(CreateOrderAddressRequest? request)
    {
        if (request is null)
            return new Address("123 Main St", "Redmond", "WA", "US", "98052");

        return new Address(
            string.IsNullOrWhiteSpace(request.Street) ? "123 Main St" : request.Street,
            string.IsNullOrWhiteSpace(request.City) ? "Redmond" : request.City,
            request.State,
            string.IsNullOrWhiteSpace(request.Country) ? "US" : request.Country,
            string.IsNullOrWhiteSpace(request.ZipCode) ? "98052" : request.ZipCode);
    }
}
