using System;
using System.Collections.Generic;
using System.Linq;
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

public class GetMyOrderRequest : BaseRequest
{
    public GetMyOrderRequest(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        BuyerId = buyerId;
        OrderId = orderId;
        CancellationToken = cancellationToken;
    }

    public string BuyerId { get; }
    public int OrderId { get; }
    public CancellationToken CancellationToken { get; }
}

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class GetMyOrderResponse : BaseResponse
{
    public GetMyOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetMyOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();
    public string? SquareOrderId { get; set; }
    public string? SquareOrderState { get; set; }

    /// <summary>"synced", "pending_square_order", "pending_gift_message" or "not_in_square".</summary>
    public string SquareSyncStatus { get; set; } = string.Empty;

    /// <summary>The gift message as Square holds it now (staff edits in Square show up here).</summary>
    public string? GiftMessage { get; set; }

    /// <summary>Set when the Square part of this response could not be read; Square fields are then incomplete.</summary>
    public string? SquareError { get; set; }
}

/// <summary>
/// The calling shopper's order with its Square order id and current gift message. Other shoppers' orders
/// are reported as not found.
/// </summary>
public class GetMyOrderEndpoint : IEndpoint<IResult, GetMyOrderRequest, SquareOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders/{orderId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext, SquareOrderService orderService) =>
            {
                var buyerId = httpContext.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                using var deadline = SquareRequestDeadline.Start(httpContext, SquareConstants.RequestBudget);
                return await HandleAsync(new GetMyOrderRequest(buyerId, orderId, deadline.Token), orderService);
            })
            .Produces<GetMyOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrderRequest request, SquareOrderService orderService)
    {
        var order = await orderService.GetMyOrderAsync(request.BuyerId, request.OrderId, request.CancellationToken);
        if (order is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new GetMyOrderResponse(request.CorrelationId())
        {
            OrderId = order.OrderId,
            OrderDate = order.OrderDate,
            Total = order.Total,
            Items = order.Items.Select(i => new MyOrderItemDto
            {
                CatalogItemId = i.CatalogItemId,
                ProductName = i.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units,
            }).ToList(),
            SquareOrderId = order.SquareOrderId,
            SquareOrderState = order.SquareOrderState,
            SquareSyncStatus = order.SquareSyncStatus,
            GiftMessage = order.GiftMessage,
            SquareError = order.SquareError,
        });
    }
}
