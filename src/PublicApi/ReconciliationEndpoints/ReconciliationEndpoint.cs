using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class ReconciliationTransactionDto
{
    public string? TransactionId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? Status { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Fee { get; set; }
    public string? EventCode { get; set; }
    public string? InitiationDate { get; set; }
    public string? PaypalReferenceId { get; set; }
}

public class ReconciliationEShopPaymentDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal Total { get; set; }
    public decimal? CapturedAmount { get; set; }
    public DateTimeOffset OrderDate { get; set; }
}

public class ReconciliationMatchDto
{
    public ReconciliationTransactionDto PayPal { get; set; } = new();
    public int? OrderId { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public ReconciliationTransactionDto[] PayPalTransactions { get; set; } = Array.Empty<ReconciliationTransactionDto>();
    public ReconciliationEShopPaymentDto[] EShopPayments { get; set; } = Array.Empty<ReconciliationEShopPaymentDto>();
    public ReconciliationMatchDto[] Matched { get; set; } = Array.Empty<ReconciliationMatchDto>();
    public ReconciliationTransactionDto[] PayPalOnly { get; set; } = Array.Empty<ReconciliationTransactionDto>();
    public ReconciliationEShopPaymentDto[] EShopOnly { get; set; } = Array.Empty<ReconciliationEShopPaymentDto>();
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service)
    {
        if (!DateTimeOffset.TryParse(request.From, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var from)
            || !DateTimeOffset.TryParse(request.To, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var to))
        {
            throw new CheckoutException("'from' and 'to' must be ISO-8601 date-times.", 400);
        }

        var report = await service.ReconcileAsync(from, to, default);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactions = report.PayPalTransactions.Select(MapTxn).ToArray(),
            EShopPayments = report.EShopPayments.Select(MapEshop).ToArray(),
            Matched = report.Matched.Select(m => new ReconciliationMatchDto { PayPal = MapTxn(m.PayPal), OrderId = m.OrderId }).ToArray(),
            PayPalOnly = report.PayPalOnly.Select(MapTxn).ToArray(),
            EShopOnly = report.EShopOnly.Select(MapEshop).ToArray()
        });
    }

    private static ReconciliationTransactionDto MapTxn(ApplicationCore.Payments.PayPalTransactionRecord t) => new()
    {
        TransactionId = t.TransactionId,
        InvoiceId = t.InvoiceId,
        CustomField = t.CustomField,
        Status = t.Status,
        Amount = t.AmountValue,
        Currency = t.Currency,
        Fee = t.FeeValue,
        EventCode = t.EventCode,
        InitiationDate = t.InitiationDate,
        PaypalReferenceId = t.PaypalReferenceId
    };

    private static ReconciliationEShopPaymentDto MapEshop(EShopPaymentRecord e) => new()
    {
        OrderId = e.OrderId,
        BuyerId = e.BuyerId,
        PaymentStatus = e.PaymentStatus,
        PayPalOrderId = e.PayPalOrderId,
        AuthorizationId = e.AuthorizationId,
        CaptureId = e.CaptureId,
        Total = e.Total,
        CapturedAmount = e.CapturedAmount,
        OrderDate = e.OrderDate
    };
}
