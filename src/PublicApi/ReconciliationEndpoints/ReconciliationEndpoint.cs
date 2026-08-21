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

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationQuery : BaseRequest
{
    public string? From { get; init; }
    public string? To { get; init; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<MatchedReconciliationRow> Matched { get; set; } = new();
    public List<PayPalReconciliationRow> PayPalOnly { get; set; } = new();
    public List<EShopReconciliationRow> EShopOnly { get; set; } = new();
}

public class MatchedReconciliationRow
{
    public PayPalReconciliationRow PayPal { get; set; } = new();
    public EShopReconciliationRow EShop { get; set; } = new();
}

public class PayPalReconciliationRow
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public decimal? Amount { get; set; }
    public decimal? FeeAmount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class EShopReconciliationRow
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public List<string> RefundIds { get; set; } = new();
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IPaymentReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IPaymentReconciliationService reconciliation) =>
                await HandleAsync(new ReconciliationQuery { From = from, To = to }, reconciliation))
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IPaymentReconciliationService reconciliation)
    {
        if (!TryParseTimestamp(request.From, out var from) || !TryParseTimestamp(request.To, out var to))
        {
            throw new CommerceException(400, "Query parameters 'from' and 'to' must be ISO-8601 date-times.");
        }

        var report = await reconciliation.ReconcileAsync(from, to);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(m => new MatchedReconciliationRow
            {
                PayPal = ToPayPal(m.PayPal),
                EShop = ToEShop(m.EShop)
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(ToPayPal).ToList(),
            EShopOnly = report.EShopOnly.Select(ToEShop).ToList()
        });
    }

    private static bool TryParseTimestamp(string? value, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result);
    }

    private static PayPalReconciliationRow ToPayPal(ApplicationCore.Payments.PayPalReportedTransaction txn)
        => new()
        {
            TransactionId = txn.TransactionId,
            ReferenceId = txn.PayPalReferenceId,
            ReferenceIdType = txn.PayPalReferenceIdType,
            EventCode = txn.EventCode,
            Status = txn.Status,
            InvoiceId = txn.InvoiceId,
            CustomField = txn.CustomField,
            Amount = txn.Amount,
            FeeAmount = txn.FeeAmount,
            Currency = txn.Currency,
            InitiationDate = txn.InitiationDate
        };

    private static EShopReconciliationRow ToEShop(EShopPaymentRecord record)
        => new()
        {
            OrderId = record.OrderId,
            Status = record.Status,
            PayPalOrderId = record.PayPalOrderId,
            AuthorizationId = record.AuthorizationId,
            CaptureId = record.CaptureId,
            RefundIds = record.RefundIds.ToList(),
            Total = record.Total,
            OrderDate = record.OrderDate
        };
}
