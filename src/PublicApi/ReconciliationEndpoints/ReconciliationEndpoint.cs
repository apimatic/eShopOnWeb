using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
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
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest
{
    public string? From { get; set; }
    public string? To { get; set; }
}

public class FinixTransactionDto
{
    public string Id { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? State { get; set; }
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string? CreatedAt { get; set; }
    public string? ParentTransfer { get; set; }
    public int? MatchedOrderId { get; set; }
    public string? MatchType { get; set; }
}

public class ReconciliationMatchDto
{
    public int OrderId { get; set; }
    public string FinixTransferId { get; set; } = string.Empty;
    public string? FinixTransferType { get; set; }
    public string? FinixTransferState { get; set; }
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string MatchedBy { get; set; } = string.Empty;
}

public class MissingInFinixDto
{
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? FinixId { get; set; }
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string? State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int FinixTransactionCount { get; set; }
    public int FinixAuthorizationCount { get; set; }
    public int MatchedCount { get; set; }
    public List<FinixTransactionDto> FinixTransactions { get; set; } = new();
    public List<FinixTransactionDto> FinixAuthorizations { get; set; } = new();
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<FinixTransactionDto> MissingInEshop { get; set; } = new();
    public List<MissingInFinixDto> MissingInFinix { get; set; } = new();
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private readonly CatalogContext _db;
    private readonly FinixClient _finix;
    private readonly IOptions<FinixOptions> _finixOptions;

    public ReconciliationEndpoint(CatalogContext db, FinixClient finix, IOptions<FinixOptions> finixOptions)
    {
        _db = db;
        _finix = finix;
        _finixOptions = finixOptions;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (string? from, string? to) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        if (!TryParse(request.From, out var from) || !TryParse(request.To, out var to))
        {
            return Results.BadRequest(new
            {
                message = "from and to must be ISO-8601 date-times, for example 2026-01-01T00:00:00Z."
            });
        }

        if (from > to)
        {
            return Results.BadRequest(new { message = "from must be earlier than or equal to to." });
        }

        var cancellationToken = CancellationToken.None;
        IReadOnlyList<FinixTransfer> finixTransfers;
        try
        {
            finixTransfers = await _finix.ListTransfersAsync(_finixOptions.Value.MerchantId, from, to,
                cancellationToken);
        }
        catch (FinixApiException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "Unable to read transactions from Finix.",
                detail: exception.Message);
        }

        IReadOnlyList<FinixAuthorization> finixAuthorizations;
        try
        {
            finixAuthorizations = await _finix.ListAuthorizationsAsync(_finixOptions.Value.MerchantId, from, to,
                cancellationToken);
        }
        catch (FinixApiException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "Unable to read authorizations from Finix.",
                detail: exception.Message);
        }

        var payments = await _db.Payments.ToListAsync(cancellationToken);
        var refunds = await _db.PaymentRefunds.ToListAsync(cancellationToken);

        var capturesByTransferId = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.FinixTransferId))
            .ToDictionary(p => p.FinixTransferId!, p => p);
        var refundsByTransferId = refunds
            .Where(r => !string.IsNullOrWhiteSpace(r.FinixRefundTransferId))
            .ToDictionary(r => r.FinixRefundTransferId, r => r);
        var paymentsByAuthorizationId = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.FinixAuthorizationId))
            .ToDictionary(p => p.FinixAuthorizationId!, p => p);
        var finixTransferIds = finixTransfers.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var finixAuthorizationIds = finixAuthorizations.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        var response = new ReconciliationResponse(Guid.NewGuid())
        {
            From = from,
            To = to
        };

        foreach (var transfer in finixTransfers)
        {
            var dto = ToDto(transfer);

            if (capturesByTransferId.TryGetValue(transfer.Id, out var capture))
            {
                dto.MatchedOrderId = capture.OrderId;
                dto.MatchType = "Capture";
                response.Matched.Add(ToMatchDto(transfer, capture.OrderId, "Capture"));
            }
            else if (refundsByTransferId.TryGetValue(transfer.Id, out var refund))
            {
                dto.MatchedOrderId = refund.OrderId;
                dto.MatchType = "Refund";
                response.Matched.Add(ToMatchDto(transfer, refund.OrderId, "Refund"));
            }
            else
            {
                dto.MatchType = "MissingInEshop";
                response.MissingInEshop.Add(dto);
            }

            response.FinixTransactions.Add(dto);
        }

        foreach (var authorization in finixAuthorizations)
        {
            var dto = new FinixTransactionDto
            {
                Id = authorization.Id,
                Type = "AUTHORIZATION",
                State = authorization.State,
                AmountCents = authorization.Amount,
                Amount = PaymentMapping.ToAmount(authorization.Amount),
                Currency = authorization.Currency,
                CreatedAt = authorization.CreatedAt
            };

            if (paymentsByAuthorizationId.TryGetValue(authorization.Id, out var payment))
            {
                dto.MatchedOrderId = payment.OrderId;
                dto.MatchType = "Authorization";
                response.Matched.Add(ToMatchDto(authorization, payment.OrderId, "Authorization"));
            }
            else
            {
                dto.MatchType = "MissingInEshop";
                response.MissingInEshop.Add(dto);
            }

            response.FinixAuthorizations.Add(dto);
        }

        foreach (var payment in payments.Where(p => !string.IsNullOrWhiteSpace(p.FinixAuthorizationId) &&
                                                     p.CreatedAt >= from && p.CreatedAt <= to))
        {
            if (!finixAuthorizationIds.Contains(payment.FinixAuthorizationId!))
            {
                response.MissingInFinix.Add(new MissingInFinixDto
                {
                    OrderId = payment.OrderId,
                    Kind = "Authorization",
                    FinixId = payment.FinixAuthorizationId,
                    AmountCents = payment.AuthorizedAmountCents,
                    Amount = PaymentMapping.ToAmount(payment.AuthorizedAmountCents),
                    State = payment.AuthorizationState,
                    CreatedAt = payment.CreatedAt
                });
            }
        }

        foreach (var payment in payments.Where(p => !string.IsNullOrWhiteSpace(p.FinixTransferId) &&
                                                              p.CapturedAt.HasValue &&
                                                              p.CapturedAt.Value >= from && p.CapturedAt.Value <= to))
        {
            if (!finixTransferIds.Contains(payment.FinixTransferId!))
            {
                response.MissingInFinix.Add(new MissingInFinixDto
                {
                    OrderId = payment.OrderId,
                    Kind = "Capture",
                    FinixId = payment.FinixTransferId,
                    AmountCents = payment.CapturedAmountCents,
                    Amount = PaymentMapping.ToAmount(payment.CapturedAmountCents),
                    State = payment.TransferState,
                    CreatedAt = payment.UpdatedAt
                });
            }
        }

        foreach (var refund in refunds.Where(r => r.CreatedAt >= from && r.CreatedAt <= to))
        {
            if (!finixTransferIds.Contains(refund.FinixRefundTransferId))
            {
                response.MissingInFinix.Add(new MissingInFinixDto
                {
                    OrderId = refund.OrderId,
                    Kind = "Refund",
                    FinixId = refund.FinixRefundTransferId,
                    AmountCents = refund.AmountCents,
                    Amount = PaymentMapping.ToAmount(refund.AmountCents),
                    State = refund.State,
                    CreatedAt = refund.CreatedAt
                });
            }
        }

        response.FinixTransactionCount = response.FinixTransactions.Count;
        response.FinixAuthorizationCount = response.FinixAuthorizations.Count;
        response.MatchedCount = response.Matched.Count;

        return Results.Ok(response);
    }

    private static bool TryParse(string? value, out DateTimeOffset result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = default;
            return false;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }

    private static FinixTransactionDto ToDto(FinixTransfer transfer)
    {
        return new FinixTransactionDto
        {
            Id = transfer.Id,
            Type = transfer.Type,
            State = transfer.State,
            AmountCents = transfer.Amount,
            Amount = PaymentMapping.ToAmount(transfer.Amount),
            Currency = transfer.Currency,
            CreatedAt = transfer.CreatedAt,
            ParentTransfer = transfer.ParentTransfer
        };
    }

    private static ReconciliationMatchDto ToMatchDto(FinixAuthorization authorization, int orderId,
        string matchedBy)
    {
        return new ReconciliationMatchDto
        {
            OrderId = orderId,
            FinixTransferId = authorization.Id,
            FinixTransferType = "AUTHORIZATION",
            FinixTransferState = authorization.State,
            AmountCents = authorization.Amount,
            Amount = PaymentMapping.ToAmount(authorization.Amount),
            MatchedBy = matchedBy
        };
    }

    private static ReconciliationMatchDto ToMatchDto(FinixTransfer transfer, int orderId, string matchedBy)
    {
        return new ReconciliationMatchDto
        {
            OrderId = orderId,
            FinixTransferId = transfer.Id,
            FinixTransferType = transfer.Type,
            FinixTransferState = transfer.State,
            AmountCents = transfer.Amount,
            Amount = PaymentMapping.ToAmount(transfer.Amount),
            MatchedBy = matchedBy
        };
    }
}
