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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationLineDto
{
    public string Side { get; set; } = string.Empty;
    public string? TransactionId { get; set; }
    public string? TransactionStatus { get; set; }
    public string? EventCode { get; set; }
    public DateTimeOffset? TransactionDate { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? Currency { get; set; }
    public int? OrderId { get; set; }
    public string? LocalStatus { get; set; }
    public decimal? LocalAmount { get; set; }
    public string? PayPalOrderId { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int LocalPaymentCount { get; set; }
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationLineDto> Lines { get; set; } = new();
}

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator report lining PayPal's own transaction
/// record up against eShop orders across the whole range. from/to are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = CallerIdentity.AdministratorsRole,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string from,
                string to,
                IReconciliationService reconciliationService,
                CancellationToken ct) =>
            {
                if (!DateTimeOffset.TryParse(from, null,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out var fromDate))
                {
                    throw new PaymentConflictException("'from' must be an ISO-8601 date-time.");
                }
                if (!DateTimeOffset.TryParse(to, null,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out var toDate))
                {
                    throw new PaymentConflictException("'to' must be an ISO-8601 date-time.");
                }
                if (toDate < fromDate)
                {
                    throw new PaymentConflictException("'to' must not be earlier than 'from'.");
                }

                var report = await reconciliationService.BuildReportAsync(fromDate, toDate, ct);
                var response = new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    PayPalTransactionCount = report.PayPalTransactionCount,
                    LocalPaymentCount = report.LocalPaymentCount,
                    MatchedCount = report.MatchedCount,
                    PayPalOnlyCount = report.PayPalOnlyCount,
                    EShopOnlyCount = report.EShopOnlyCount,
                    Lines = report.Lines.Select(l => new ReconciliationLineDto
                    {
                        Side = l.Side.ToString(),
                        TransactionId = l.TransactionId,
                        TransactionStatus = l.TransactionStatus,
                        EventCode = l.EventCode,
                        TransactionDate = l.TransactionDate,
                        PayPalAmount = l.PayPalAmount,
                        Currency = l.Currency,
                        OrderId = l.OrderId,
                        LocalStatus = l.LocalStatus?.ToString(),
                        LocalAmount = l.LocalAmount,
                        PayPalOrderId = l.PayPalOrderId
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("PaymentEndpoints");
    }
}
