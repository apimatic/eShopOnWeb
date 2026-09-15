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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, string, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IOrderCheckoutService checkout) =>
                await HandleAsync(from, to, checkout))
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(string from, IOrderCheckoutService checkout)
        => Task.FromResult(Results.BadRequest());

    private static async Task<IResult> HandleAsync(string from, string to, IOrderCheckoutService checkout)
    {
        if (!TryParseTimestamp(from, out var fromDate) || !TryParseTimestamp(to, out var toDate))
        {
            throw new PaymentException(400, "from and to must be ISO-8601 date-times.");
        }

        var report = await checkout.ReconcileAsync(fromDate, toDate);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(m => new ReconciliationMatchResponse
            {
                OrderId = m.OrderId,
                PayPalTransactionId = m.PayPalTransaction.TransactionId,
                PayPalReferenceId = m.PayPalTransaction.PaypalReferenceId,
                EventCode = m.PayPalTransaction.TransactionEventCode,
                Status = m.PayPalTransaction.TransactionStatus,
                InvoiceId = m.PayPalTransaction.InvoiceId,
                Amount = m.PayPalTransaction.Amount,
                Currency = m.PayPalTransaction.Currency,
                FeeAmount = m.PayPalTransaction.FeeAmount,
                InitiationDate = m.PayPalTransaction.InitiationDate
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(t => new ReconciliationPayPalOnlyResponse
            {
                PayPalTransactionId = t.TransactionId,
                PayPalReferenceId = t.PaypalReferenceId,
                EventCode = t.TransactionEventCode,
                Status = t.TransactionStatus,
                InvoiceId = t.InvoiceId,
                CustomField = t.CustomField,
                Amount = t.Amount,
                Currency = t.Currency,
                FeeAmount = t.FeeAmount,
                InitiationDate = t.InitiationDate
            }).ToList(),
            EShopOnly = report.EShopOnly.Select(e => new ReconciliationEShopOnlyResponse
            {
                OrderId = e.OrderId,
                PayPalOrderId = e.PayPalOrderId,
                AuthorizationId = e.AuthorizationId,
                CaptureId = e.CaptureId,
                RefundId = e.RefundId,
                PaymentStatus = e.PaymentStatus,
                Amount = e.Amount,
                Currency = e.Currency
            }).ToList()
        });
    }

    private static bool TryParseTimestamp(string value, out DateTimeOffset parsed)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed);
    }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationMatchResponse> Matched { get; set; } = new();
    public List<ReconciliationPayPalOnlyResponse> PayPalOnly { get; set; } = new();
    public List<ReconciliationEShopOnlyResponse> EShopOnly { get; set; } = new();
}

public class ReconciliationMatchResponse
{
    public int OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? PayPalReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public string? InvoiceId { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public string? FeeAmount { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class ReconciliationPayPalOnlyResponse
{
    public string? PayPalTransactionId { get; set; }
    public string? PayPalReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public string? FeeAmount { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class ReconciliationEShopOnlyResponse
{
    public int OrderId { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public string? RefundId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
