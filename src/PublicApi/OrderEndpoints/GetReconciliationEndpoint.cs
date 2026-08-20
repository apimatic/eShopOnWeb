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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetReconciliationEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly IOrderPaymentService _orders;

    public GetReconciliationEndpoint(IOrderPaymentService orders)
    {
        _orders = orders;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, HttpContext httpContext) =>
            {
                return await HandleAsync(from, to, httpContext);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(HttpContext httpContext) => Task.FromResult(Results.BadRequest());

    public async Task<IResult> HandleAsync(string from, string to, HttpContext httpContext)
    {
        if (!TryParseTime(from, out var fromValue) || !TryParseTime(to, out var toValue))
        {
            throw new PaymentValidationException("from and to must be ISO-8601 date-times.");
        }

        var report = await _orders.ReconcileAsync(fromValue, toValue, httpContext.RequestAborted);
        return Results.Ok(ReconciliationResponse.Create(report));
    }

    private static bool TryParseTime(string value, out DateTimeOffset parsed)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed);
    }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<ReconciliationMatchDto> Matched { get; set; } = new();
    public System.Collections.Generic.List<PayPalTransactionDto> PayPalOnly { get; set; } = new();
    public System.Collections.Generic.List<EshopOnlyDto> EshopOnly { get; set; } = new();

    public static ReconciliationResponse Create(ReconciliationReport report) => new()
    {
        From = report.From,
        To = report.To,
        Matched = report.Matched.Select(m => new ReconciliationMatchDto
        {
            OrderId = m.OrderId,
            PayPalTransaction = PayPalTransactionDto.From(m.PayPalTransaction)
        }).ToList(),
        PayPalOnly = report.PayPalOnly.Select(PayPalTransactionDto.From).ToList(),
        EshopOnly = report.EshopOnly.Select(e => new EshopOnlyDto
        {
            OrderId = e.OrderId,
            Status = e.Status,
            PayPalOrderId = e.PayPalOrderId,
            AuthorizationId = e.AuthorizationId,
            CaptureId = e.CaptureId,
            OrderDate = e.OrderDate,
            Total = e.Total
        }).ToList()
    };
}

public class ReconciliationMatchDto
{
    public int OrderId { get; set; }
    public PayPalTransactionDto PayPalTransaction { get; set; } = new();
}

public class PayPalTransactionDto
{
    public string? TransactionId { get; set; }
    public string? PayPalReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }

    public static PayPalTransactionDto From(PayPalReportedTransaction txn) => new()
    {
        TransactionId = txn.TransactionId,
        PayPalReferenceId = txn.PayPalReferenceId,
        InvoiceId = txn.InvoiceId,
        CustomField = txn.CustomField,
        EventCode = txn.EventCode,
        Status = txn.Status,
        Amount = txn.Amount,
        Currency = txn.Currency,
        InitiationDate = txn.InitiationDate
    };
}

public class EshopOnlyDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
}
