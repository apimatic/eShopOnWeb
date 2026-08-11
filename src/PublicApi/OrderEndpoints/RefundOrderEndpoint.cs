using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds the caller's own captured order, in full or in part. Carries a caller-supplied idempotency
/// key: repeating a request under the same key never refunds twice, while two distinct partial refunds
/// remain legitimate. Returns the created <c>refundId</c> as a top-level field.
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
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service);
            })
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentOperationException("A refund requires a caller-supplied 'idempotencyKey'.");
        }
        if (request.Amount is <= 0)
        {
            throw new PaymentOperationException("Refund 'amount', when supplied, must be greater than zero.");
        }

        var order = await service.RefundAsync(request.OrderId, buyerId, request.Amount, request.IdempotencyKey);
        var refund = order.FindRefundByIdempotencyKey(request.IdempotencyKey)
                     ?? throw new PaymentResourceNotFoundException("The refund could not be located after processing.");

        var response = new RefundResponse
        {
            RefundId = refund.PayPalRefundId,
            OrderId = order.Id,
            Status = refund.Status,
            Amount = refund.Amount,
            Currency = order.Currency,
            TotalRefunded = order.TotalRefunded(),
            RefundableRemaining = order.RefundableRemaining()
        };
        return Results.Created($"api/orders/{order.Id}/refunds/{refund.PayPalRefundId}", response);
    }
}
