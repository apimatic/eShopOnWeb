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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator: PayPal's own record of transactions over a date range, lined up against eShop
/// orders. Covers the whole range (all pages, chunked to PayPal's 31-day window). Note that
/// PayPal transaction reporting lags live activity, so very recent payments may not appear yet.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService, CancellationToken ct) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), reconciliationService, ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService reconciliationService)
    {
        return HandleAsync(request, reconciliationService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService reconciliationService, CancellationToken ct)
    {
        try
        {
            var report = await reconciliationService.GetReportAsync(request.From, request.To, ct);

            var response = new ReconciliationResponse(request.CorrelationId())
            {
                From = report.From,
                To = report.To,
                Transactions = report.Transactions.Select(MapLine).ToList(),
                UnmatchedTransactions = report.UnmatchedTransactions.Select(MapLine).ToList(),
                OrdersMissingFromPayPalReport = report.OrdersMissingFromPayPalReport.Select(o => new UnmatchedOrderDto
                {
                    OrderId = o.OrderId,
                    PayPalOrderId = o.PayPalOrderId,
                    AuthorizationId = o.AuthorizationId,
                    CaptureId = o.CaptureId,
                    PaymentStatus = o.PaymentStatus,
                    OrderDate = o.OrderDate
                }).ToList()
            };
            return Results.Ok(response);
        }
        catch (Exception ex) when (EndpointErrorMapper.TryMap(ex, out var error))
        {
            return error;
        }
    }

    private static ReconciliationTransactionDto MapLine(ReconciliationLine line)
    {
        return new ReconciliationTransactionDto
        {
            TransactionId = line.Transaction.TransactionId,
            ReferenceId = line.Transaction.ReferenceId,
            ReferenceIdType = line.Transaction.ReferenceIdType,
            InvoiceId = line.Transaction.InvoiceId,
            CustomId = line.Transaction.CustomId,
            Amount = line.Transaction.Amount,
            Currency = line.Transaction.Currency,
            Fee = line.Transaction.Fee,
            Status = line.Transaction.Status,
            EventCode = line.Transaction.EventCode,
            InitiatedAt = line.Transaction.InitiatedAt,
            OrderId = line.OrderId
        };
    }
}

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconciliationTransactionDto
{
    public string? TransactionId { get; set; }
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public string? Status { get; set; }
    public string? EventCode { get; set; }
    public DateTimeOffset? InitiatedAt { get; set; }

    /// <summary>The local order this transaction matched, or null if no eShop order claims it.</summary>
    public int? OrderId { get; set; }
}

public class UnmatchedOrderDto
{
    public int OrderId { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>PayPal transactions matched to a local order.</summary>
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new();

    /// <summary>PayPal transactions no local order claims.</summary>
    public List<ReconciliationTransactionDto> UnmatchedTransactions { get; set; } = new();

    /// <summary>Local orders with PayPal payment ids that never appeared in PayPal's report.</summary>
    public List<UnmatchedOrderDto> OrdersMissingFromPayPalReport { get; set; } = new();
}
