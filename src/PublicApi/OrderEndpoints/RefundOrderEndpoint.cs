using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, [FromBody] RefundOrderRequest request, IOrderPaymentService payments, ClaimsPrincipal user, HttpRequest http) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.OrderId = orderId;
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    && http.Headers.TryGetValue("Idempotency-Key", out var headerKey))
                {
                    request.IdempotencyKey = headerKey.ToString();
                }

                return await HandleAsync(request, payments, buyerId);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService payments) =>
        HandleAsync(request, payments, string.Empty);

    private async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService payments, string buyerId)
    {
        var (order, refund) = await payments.RefundOrderAsync(
            request.OrderId,
            buyerId,
            request.IdempotencyKey,
            request.Amount);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            Order = OrderDto.From(order),
            Refund = new RefundDto
            {
                RefundId = refund.Id,
                PayPalRefundId = refund.PayPalRefundId,
                Status = refund.Status,
                Amount = refund.Amount,
                Currency = refund.Currency,
                CreatedAt = refund.CreatedAt
            }
        };
        return Results.Created($"api/orders/{order.Id}/refunds/{refund.Id}", response);
    }
}
