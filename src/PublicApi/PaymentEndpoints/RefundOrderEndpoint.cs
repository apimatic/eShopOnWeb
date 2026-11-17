using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ClaimsPrincipal>
{
    private readonly CatalogContext _db;
    private readonly FinixPaymentService _payments;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(
        CatalogContext db,
        FinixPaymentService payments,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _payments = payments;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, RefundOrderRequest request, ClaimsPrincipal user) =>
                {
                    request.OrderId = orderId;
                    return await HandleAsync(request, user);
                })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return PaymentResults.Unauthorized();
        }

        var order = await OrderQueries.LoadOrderAsync(_db, request.OrderId, CancellationToken.None);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        var payment = await OrderQueries.LoadPaymentAsync(_db, order.Id, CancellationToken.None);
        if (payment is null)
        {
            return Results.Conflict(new { message = "This order has no payment record." });
        }

        var idempotencyKey = request.EffectiveIdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var headers = _httpContextAccessor.HttpContext?.Request.Headers;
            idempotencyKey = headers?["Idempotency-Key"].FirstOrDefault()
                             ?? headers?["Idempotency_Key"].FirstOrDefault()
                             ?? headers?["X-Idempotency-Key"].FirstOrDefault();
        }

        try
        {
            var refund = await _payments.RefundAsync(
                order,
                payment,
                request.EffectiveAmountCents,
                idempotencyKey ?? string.Empty,
                CancellationToken.None);

            var refunds = await OrderQueries.LoadRefundsAsync(_db, payment.Id, CancellationToken.None);
            var totalRefunded = refunds.Sum(r => r.AmountCents);
            var response = new RefundOrderResponse
            {
                OrderId = order.Id,
                RefundId = refund.PublicId,
                FinixTransferId = refund.FinixTransferId,
                State = refund.FinixState,
                AmountCents = refund.AmountCents,
                TotalRefundedCents = totalRefunded,
                RemainingRefundableCents = payment.CapturedAmountCents - totalRefunded,
                Payment = PaymentResponseMapper.ToPaymentResponse(payment, refunds)
            };

            return Results.Created($"api/orders/{order.Id}/refunds/{refund.PublicId}", response);
        }
        catch (PaymentOperationException exception)
        {
            return PaymentResults.FromException(exception);
        }
    }
}
