using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using Microsoft.eShopWeb.PublicApi.SquareEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemDto> Items { get; set; } = new();

    /// <summary>Optional, at most 200 characters. Stored on the Square order (field "Gift message"), not in eShop.</summary>
    public string? GiftMessage { get; set; }

    /// <summary>Optional; omitted for orders collected in person.</summary>
    public ShipToAddressDto? ShipToAddress { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public string? SquareOrderId { get; set; }

    /// <summary>"synced", "pending_square_order" or "pending_gift_message".</summary>
    public string SquareSyncStatus { get; set; } = string.Empty;
    public string? Notice { get; set; }
}

/// <summary>
/// Places an order for the calling shopper from catalog items, using eShop's order model, and creates it
/// in Square at the merchant's location with the gift message.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, SquareOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext, SquareOrderService orderService) =>
            {
                var buyerId = httpContext.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                using var deadline = SquareRequestDeadline.Start(httpContext, SquareConstants.RequestBudget);
                request.BuyerId = buyerId;
                request.CancellationToken = deadline.Token;
                return await HandleAsync(request, orderService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces<CreateOrderResponse>(StatusCodes.Status202Accepted)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, SquareOrderService orderService)
    {
        var address = request.ShipToAddress is { } a
            ? new ShippingAddress(a.Street, a.City, a.State, a.Country, a.ZipCode)
            : null;
        var placed = await orderService.PlaceOrderAsync(
            request.BuyerId,
            (request.Items ?? new List<CreateOrderItemDto>()).Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList(),
            request.GiftMessage,
            address,
            request.CancellationToken);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = placed.OrderId,
            SquareOrderId = placed.SquareOrderId,
            SquareSyncStatus = placed.SquareSyncStatus,
            Notice = placed.Notice,
        };

        return placed.SquareSyncStatus == "synced"
            ? Results.Created($"api/my-orders/{placed.OrderId}", response)
            : Results.Accepted($"api/my-orders/{placed.OrderId}", response);
    }
}
