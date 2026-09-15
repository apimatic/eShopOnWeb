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

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications.OrderEndpoints;

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

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();

    /// <summary>Optional. A sensible default is used when omitted.</summary>
    public ShippingAddressDto? ShipToAddress { get; set; }

    /// <summary>The shopper placing the order. Set from the token, never from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

/// <summary>
/// POST /api/orders &ndash; place an order from catalog item ids and quantities for the signed-in shopper,
/// then tell them it was placed. Reuses the app's existing order/order-item model.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IApiOrderService, IOrderNotificationService>
{
    // Mirrors the storefront's default checkout address, used when the caller supplies none.
    private static readonly ShippingAddressDto DefaultAddress = new()
    {
        Street = "123 Main St.",
        City = "Kent",
        State = "OH",
        Country = "United States",
        ZipCode = "44240"
    };

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IApiOrderService orderService, IOrderNotificationService notificationService) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                request.BuyerId = buyerId;
                return await HandleAsync(request, orderService, notificationService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IApiOrderService orderService, IOrderNotificationService notificationService)
    {
        var a = request.ShipToAddress ?? DefaultAddress;
        var address = new Address(a.Street, a.City, a.State, a.Country, a.ZipCode);
        var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();

        var order = await orderService.PlaceOrderAsync(request.BuyerId, lines, address);

        // Tell the shopper their order was placed (a send failure here never fails order placement).
        await notificationService.NotifyOrderPlacedAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
