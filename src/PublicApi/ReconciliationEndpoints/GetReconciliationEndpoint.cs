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

public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IPaymentReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IPaymentReconciliationService service) =>
                await HandleAsync(new ReconciliationQuery(from, to), service))
            .Produces<ReconciliationApiResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery query, IPaymentReconciliationService service)
    {
        if (!TryParse(query.From, out var from))
        {
            throw new PaymentException("`from` must be an ISO-8601 date-time.", 400);
        }

        if (!TryParse(query.To, out var to))
        {
            throw new PaymentException("`to` must be an ISO-8601 date-time.", 400);
        }

        var report = await service.ReconcileAsync(from, to);
        return Results.Ok(new ReconciliationApiResponse
        {
            From = report.From,
            To = report.To,
            Matches = report.Matches.Select(m => new ReconciliationMatchApiResponse
            {
                OrderId = m.OrderId,
                PayPalTransactionId = m.PayPalTransaction.TransactionId,
                ReferenceId = m.PayPalTransaction.ReferenceId,
                EventCode = m.PayPalTransaction.EventCode,
                Status = m.PayPalTransaction.Status,
                InvoiceId = m.PayPalTransaction.InvoiceId,
                CustomField = m.PayPalTransaction.CustomField,
                Amount = m.PayPalTransaction.Amount?.Value,
                Currency = m.PayPalTransaction.Amount?.CurrencyCode,
                FeeAmount = m.PayPalTransaction.FeeAmount?.Value,
                InitiationDate = m.PayPalTransaction.InitiationDate
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(t => new ReconciliationPayPalOnlyApiResponse
            {
                PayPalTransactionId = t.TransactionId,
                ReferenceId = t.ReferenceId,
                EventCode = t.EventCode,
                Status = t.Status,
                InvoiceId = t.InvoiceId,
                CustomField = t.CustomField,
                Amount = t.Amount?.Value,
                Currency = t.Amount?.CurrencyCode,
                InitiationDate = t.InitiationDate
            }).ToList(),
            EshopOnly = report.EshopOnly.Select(e => new ReconciliationEshopOnlyApiResponse
            {
                OrderId = e.OrderId,
                PayPalCaptureId = e.PayPalCaptureId,
                PayPalAuthorizationId = e.PayPalAuthorizationId,
                Status = e.Status,
                Amount = e.Amount
            }).ToList()
        });
    }

    private static bool TryParse(string value, out DateTimeOffset parsed)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out parsed);
    }
}

public record ReconciliationQuery(string From, string To);

public class ReconciliationApiResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<ReconciliationMatchApiResponse> Matches { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationPayPalOnlyApiResponse> PayPalOnly { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationEshopOnlyApiResponse> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchApiResponse
{
    public int OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? ReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public string? FeeAmount { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class ReconciliationPayPalOnlyApiResponse
{
    public string? PayPalTransactionId { get; set; }
    public string? ReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class ReconciliationEshopOnlyApiResponse
{
    public int OrderId { get; set; }
    public string? PayPalCaptureId { get; set; }
    public string? PayPalAuthorizationId { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
}
