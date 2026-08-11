using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public int? MatchedOrderId { get; set; }
    public string? OrderPaymentState { get; set; }
}

public class ReconciliationUnmatchedOrderDto
{
    public int OrderId { get; set; }
    public string PaymentState { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }

    /// <summary>Transactions matched to an eShop order.</summary>
    public List<ReconciliationTransactionDto> Matched { get; set; } = new();

    /// <summary>Transactions PayPal reported that eShop could not match to an order.</summary>
    public List<ReconciliationTransactionDto> PayPalOnly { get; set; } = new();

    /// <summary>eShop payments PayPal has not reported for this range (expected for very recent activity).</summary>
    public List<ReconciliationUnmatchedOrderDto> EShopOnly { get; set; } = new();
}

/// <summary>
/// Operator report: lists PayPal's own transactions for a date range and lines them up against
/// eShop orders. Covers the whole range (chunked and fully paginated).
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Reconciles PayPal transactions against eShop orders (operator)", Tags = new[] { "ReconciliationEndpoints" })]
            async (string from, string to, IReconciliationService reconciliation) =>
            {
                var fromDate = ParseIso(from, nameof(from));
                var toDate = ParseIso(to, nameof(to));

                var report = await reconciliation.ReconcileAsync(fromDate, toDate);

                return Results.Ok(new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    PayPalTransactionCount = report.PayPalTransactionCount,
                    MatchedCount = report.Matched.Count,
                    Matched = report.Matched.Select(m => new ReconciliationTransactionDto
                    {
                        TransactionId = m.Transaction.TransactionId,
                        Status = m.Transaction.Status,
                        Amount = m.Transaction.Amount,
                        Currency = m.Transaction.Currency,
                        FeeAmount = m.Transaction.FeeAmount,
                        InvoiceId = m.Transaction.InvoiceId,
                        CustomField = m.Transaction.CustomField,
                        InitiationDate = m.Transaction.InitiationDate,
                        MatchedOrderId = m.OrderId,
                        OrderPaymentState = m.PaymentState
                    }).ToList(),
                    PayPalOnly = report.PayPalOnly.Select(t => new ReconciliationTransactionDto
                    {
                        TransactionId = t.TransactionId,
                        Status = t.Status,
                        Amount = t.Amount,
                        Currency = t.Currency,
                        FeeAmount = t.FeeAmount,
                        InvoiceId = t.InvoiceId,
                        CustomField = t.CustomField,
                        InitiationDate = t.InitiationDate
                    }).ToList(),
                    EShopOnly = report.EShopOnly.Select(o => new ReconciliationUnmatchedOrderDto
                    {
                        OrderId = o.OrderId,
                        PaymentState = o.PaymentState,
                        Amount = o.Amount,
                        Currency = o.Currency,
                        AuthorizationId = o.AuthorizationId,
                        CaptureId = o.CaptureId
                    }).ToList()
                });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    private static DateTimeOffset ParseIso(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new PaymentValidationException(
                $"'{name}' must be an ISO-8601 date-time, e.g. 2026-08-01T00:00:00Z.");
        }
        return parsed;
    }
}
