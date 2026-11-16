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
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Finix;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateRefundRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    public decimal? Amount { get; set; }
    public decimal? RefundAmount { get; set; }
    public long? AmountCents { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? Key { get; set; }
}

public class CreateRefundResponse : BaseResponse
{
    public CreateRefundResponse(Guid correlationId) : base(correlationId) { }

    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public string RefundTransferId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public long AmountCents { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal RefundedTotal { get; set; }
    public decimal RemainingRefundable { get; set; }
    public PaymentDto? Payment { get; set; }
}

public class CreateRefundEndpoint : IEndpoint<IResult, CreateRefundRequest>
{
    private readonly CatalogContext _db;
    private readonly FinixClient _finix;
    private readonly IOptions<FinixOptions> _finixOptions;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateRefundEndpoint(CatalogContext db, FinixClient finix, IOptions<FinixOptions> finixOptions,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _finix = finix;
        _finixOptions = finixOptions;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CreateRefundRequest request) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request);
            })
            .Produces<CreateRefundResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateRefundRequest request)
    {
        var ownerId = PaymentHelpers.OwnerId(_httpContextAccessor.HttpContext?.User);
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Results.Unauthorized();
        }

        var idempotencyKey = request.IdempotencyKey ?? request.Key ??
                             _httpContextAccessor.HttpContext?.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotencyKey is required for refunds." });
        }

        var cancellationToken = CancellationToken.None;
        var orderLock = PaymentHelpers.LockForOrder(request.OrderId);
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await _db.Orders
                .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
                .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);

            if (order is null || !string.Equals(order.BuyerId, ownerId, StringComparison.Ordinal))
            {
                return Results.NotFound();
            }

            var payment = await _db.Payments
                .FirstOrDefaultAsync(p => p.OrderId == order.Id, cancellationToken);

            var existingRefund = await _db.PaymentRefunds
                .FirstOrDefaultAsync(r => r.OrderId == order.Id && r.IdempotencyKey == idempotencyKey,
                    cancellationToken);

            if (existingRefund is not null)
            {
                return Results.Ok(await BuildResponseAsync(order, payment, existingRefund, cancellationToken));
            }

            if (payment is null || payment.CapturedAmountCents <= 0 || string.IsNullOrWhiteSpace(payment.FinixTransferId))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "The order has no captured payment to refund.",
                    detail: $"The order is currently {order.Status}.");
            }

            if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "The order is not refundable.",
                    detail: $"Only fulfilled orders with captured funds can be refunded. The order is currently {order.Status}.");
            }

            var remaining = payment.RefundableAmountCents;
            if (remaining <= 0)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "The captured payment has already been fully refunded.",
                    detail: "No refundable balance remains for this order.");
            }

            long amountCents;
            if (request.AmountCents.HasValue)
            {
                amountCents = request.AmountCents.Value;
            }
            else if (request.Amount.HasValue || request.RefundAmount.HasValue)
            {
                amountCents = PaymentMapping.ToCents((request.Amount ?? request.RefundAmount)!.Value);
            }
            else
            {
                amountCents = remaining;
            }

            if (amountCents <= 0)
            {
                return Results.BadRequest(new { message = "The refund amount must be greater than zero." });
            }

            if (amountCents > remaining)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "The refund amount exceeds the refundable balance.",
                    detail: $"Requested {PaymentMapping.ToAmount(amountCents):0.00} but only " +
                            $"{PaymentMapping.ToAmount(remaining):0.00} remains refundable.");
            }

            if (amountCents < payment.CapturedAmountCents)
            {
                var settled = await EnsureTransferSettledAsync(payment, cancellationToken);
                if (!settled)
                {
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                        title: "The captured transfer is still pending at Finix.",
                        detail: "Finix only accepts partial refunds after the capture has succeeded. " +
                                "Retry the refund shortly or request a full refund.");
                }
            }

            FinixTransfer refundTransfer;
            try
            {
                refundTransfer = await _finix.CreateRefundAsync(payment.FinixTransferId, amountCents, cancellationToken);
            }
            catch (FinixApiException exception)
            {
                return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                    title: "The refund could not be created with Finix.",
                    detail: exception.Message);
            }

            if (refundTransfer.State is "FAILED" or "CANCELED")
            {
                return Results.Problem(statusCode: StatusCodes.Status402PaymentRequired,
                    title: "The refund failed.",
                    detail: refundTransfer.FailureMessage ?? "Finix rejected the refund.");
            }

            var refund = new PaymentRefund(payment.Id, order.Id, refundTransfer.Id, amountCents,
                refundTransfer.State ?? "PENDING", idempotencyKey);
            _db.PaymentRefunds.Add(refund);
            payment.RecordRefund(amountCents);

            if (payment.RefundedAmountCents >= payment.CapturedAmountCents)
            {
                order.MarkRefunded();
            }
            else
            {
                order.MarkPartiallyRefunded();
            }

            await _db.SaveChangesAsync(cancellationToken);

            var response = await BuildResponseAsync(order, payment, refund, cancellationToken);
            return Results.Created($"api/orders/{order.Id}/refunds/{refund.Id}", response);
        }
        finally
        {
            orderLock.Release();
        }
    }

    private async Task<bool> EnsureTransferSettledAsync(Payment payment, CancellationToken cancellationToken)
    {
        if (payment.TransferState == "SUCCEEDED")
        {
            return true;
        }

        for (var attempt = 0; attempt < 120; attempt++)
        {
            try
            {
                var transfer = await _finix.GetTransferAsync(payment.FinixTransferId!, cancellationToken);
                payment.UpdateTransferState(transfer.State);

                if (transfer.State == "SUCCEEDED")
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    return true;
                }

                if (transfer.State is "FAILED" or "CANCELED")
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    return false;
                }
            }
            catch (FinixApiException)
            {
            }

            if (attempt < 119)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return payment.TransferState == "SUCCEEDED";
    }

    private async Task<CreateRefundResponse> BuildResponseAsync(Order order, Payment? payment,
        PaymentRefund refund, CancellationToken cancellationToken)
    {
        var refunds = payment is null
            ? new List<PaymentRefund>()
            : await _db.PaymentRefunds.Where(r => r.PaymentId == payment.Id).ToListAsync(cancellationToken);

        return new CreateRefundResponse(Guid.NewGuid())
        {
            RefundId = refund.Id,
            OrderId = order.Id,
            RefundTransferId = refund.FinixRefundTransferId,
            Amount = PaymentMapping.ToAmount(refund.AmountCents),
            AmountCents = refund.AmountCents,
            Status = refund.State,
            RefundedTotal = payment is null ? 0 : PaymentMapping.ToAmount(payment.RefundedAmountCents),
            RemainingRefundable = payment is null ? 0 : PaymentMapping.ToAmount(payment.RefundableAmountCents),
            Payment = PaymentMapping.ToDto(order, payment, refunds, _finixOptions.Value.Currency)
        };
    }
}
