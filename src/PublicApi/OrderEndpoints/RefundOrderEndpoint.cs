using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    public RefundOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// Fully refunds an order's PayPal payment. Idempotent: a repeated request never produces a second refund.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new RefundOrderRequest(orderId), paymentService);
            })
            .Produces<RefundOrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService paymentService)
    {
        var buyerId = BuyerIdAccessor.GetBuyerId(_httpContextAccessor.HttpContext?.User);
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        Order? order;
        try
        {
            order = await paymentService.RefundAsync(buyerId, request.OrderId);
        }
        catch (PaymentGatewayException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }

        if (order is null) return Results.NotFound(new { message = $"Order {request.OrderId} was not found." });

        return Results.Ok(new RefundOrderResponse(request.CorrelationId()) { Order = order.ToDto() });
    }
}
