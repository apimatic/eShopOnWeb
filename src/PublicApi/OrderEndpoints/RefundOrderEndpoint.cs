using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Returns a fulfilled order: refunds the captured payment in full (no amount) or in part.
/// The caller-supplied idempotency key guarantees a repeated request never refunds twice.
/// Callable by the shopper who owns the order or by an operator.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IRepository<Order>, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IRepository<Order> orderRepository, IOrderPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.Username = OrderMapping.GetUserName(user);
                request.IsAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, orderRepository, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IRepository<Order> orderRepository, IOrderPaymentService paymentService)
    {
        if (string.IsNullOrEmpty(request.Username))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new RefundOrderResponse { Message = "idempotencyKey is required." });
        }
        if (request.Amount.HasValue && request.Amount.Value <= 0)
        {
            return Results.BadRequest(new RefundOrderResponse { Message = "amount must be positive." });
        }

        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order == null || (!request.IsAdmin && order.BuyerId != request.Username))
        {
            return Results.NotFound(new RefundOrderResponse { Message = $"Order {request.OrderId} was not found." });
        }

        try
        {
            var refund = await paymentService.RefundPaymentAsync(order, request.Amount, request.IdempotencyKey, request.NoteToPayer);
            return Results.Ok(new RefundOrderResponse
            {
                RefundId = refund.Id,
                PayPalRefundId = refund.PayPalRefundId,
                OrderId = order.Id,
                Status = refund.PayPalStatus,
                Amount = refund.Amount,
                IdempotencyKey = refund.IdempotencyKey
            });
        }
        catch (OrderStateException ex)
        {
            return Results.Conflict(new RefundOrderResponse { Message = ex.Message });
        }
        catch (PaymentException ex)
        {
            return Results.UnprocessableEntity(new RefundOrderResponse { Message = ex.Message });
        }
    }
}

public class RefundOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string? Username { get; set; }

    [JsonIgnore]
    public bool IsAdmin { get; set; }

    /// <summary>Partial amount to refund; omit to refund everything still refundable.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key; repeating the request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? NoteToPayer { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public int RefundId { get; set; }
    public string? PayPalRefundId { get; set; }
    public int OrderId { get; set; }
    public string? Status { get; set; }
    public decimal Amount { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? Message { get; set; }
}
