using System;
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

public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery>
{
    private readonly IReconciliationService _reconciliationService;

    public GetReconciliationEndpoint(IReconciliationService reconciliationService)
    {
        _reconciliationService = reconciliationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to) => await HandleAsync(new ReconciliationQuery(from, to)))
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery query)
    {
        if (!TryParseTimestamp(query.From, out var from))
        {
            throw new PaymentException(400, "`from` must be an ISO-8601 date-time.");
        }

        if (!TryParseTimestamp(query.To, out var to))
        {
            throw new PaymentException(400, "`to` must be an ISO-8601 date-time.");
        }

        var report = await _reconciliationService.ReconcileAsync(from, to);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactions = report.PayPalTransactions.Select(MapPayPal).ToList(),
            Matches = report.Matches.Select(m => new ReconciliationMatchDto
            {
                PayPal = MapPayPal(m.PayPal),
                Eshop = MapEshop(m.Eshop)
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(MapPayPal).ToList(),
            EshopOnly = report.EshopOnly.Select(MapEshop).ToList()
        });
    }

    private static bool TryParseTimestamp(string? value, out DateTimeOffset parsed)
    {
        parsed = default;
        return !string.IsNullOrWhiteSpace(value)
               && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed);
    }

    private static PayPalTransactionDto MapPayPal(PayPalReportedTransaction tx) => new()
    {
        TransactionId = tx.TransactionId,
        ReferenceId = tx.ReferenceId,
        InvoiceId = tx.InvoiceId,
        CustomField = tx.CustomField,
        EventCode = tx.EventCode,
        Status = tx.Status,
        Amount = tx.Amount,
        Currency = tx.Currency,
        InitiationDate = tx.InitiationDate,
        Fee = tx.Fee
    };

    private static EshopReconciliationDto MapEshop(EshopReconciliationEntry entry) => new()
    {
        OrderId = entry.OrderId,
        Status = entry.Status.ToString(),
        PayPalOrderId = entry.PayPalOrderId,
        AuthorizationId = entry.AuthorizationId,
        CaptureId = entry.CaptureId,
        RefundIds = entry.RefundIds.ToList(),
        InvoiceId = entry.InvoiceId
    };
}

public record ReconciliationQuery(string? From, string? To);

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<PayPalTransactionDto> PayPalTransactions { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationMatchDto> Matches { get; set; } = new();
    public System.Collections.Generic.List<PayPalTransactionDto> PayPalOnly { get; set; } = new();
    public System.Collections.Generic.List<EshopReconciliationDto> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public PayPalTransactionDto PayPal { get; set; } = new();
    public EshopReconciliationDto Eshop { get; set; } = new();
}

public class PayPalTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public decimal? Fee { get; set; }
}

public class EshopReconciliationDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public System.Collections.Generic.List<string> RefundIds { get; set; } = new();
    public string? InvoiceId { get; set; }
}
