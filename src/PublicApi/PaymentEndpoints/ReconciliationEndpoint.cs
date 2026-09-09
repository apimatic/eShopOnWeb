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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: reports PayPal's own record of transactions for a date range and lines them
/// up against eShop orders, so a payment one side knows about and the other doesn't is visible.
/// Covers the whole range (all pages), not just the first page.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IPaymentService paymentService) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, paymentService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService paymentService)
    {
        var response = new ReconciliationResponse(request.CorrelationId());

        var from = ParseIso(request.From, nameof(request.From));
        var to = ParseIso(request.To, nameof(request.To));

        var report = await paymentService.ReconcileAsync(from, to);

        response.From = report.From;
        response.To = report.To;
        response.GeneratedAt = report.GeneratedAt;
        response.PayPalTransactionCount = report.PayPalTransactionCount;
        response.Note = report.Note;
        response.Matched = report.Matched.Select(ToDto).ToList();
        response.InPayPalNotInEShop = report.InPayPalNotInEShop.Select(ToDto).ToList();
        response.InEShopNotInPayPal = report.InEShopNotInPayPal
            .Select(o => new UnreconciledOrderDto
            {
                OrderId = o.OrderId,
                InvoiceId = o.InvoiceId,
                CaptureId = o.CaptureId,
                CapturedAmount = o.CapturedAmount,
                CurrencyCode = o.CurrencyCode,
                Status = o.Status
            }).ToList();

        return Results.Ok(response);
    }

    private static ReconciliationEntryDto ToDto(ReconciliationEntry e) => new()
    {
        TransactionId = e.TransactionId,
        Status = e.Status,
        Amount = e.Amount,
        CurrencyCode = e.CurrencyCode,
        FeeAmount = e.FeeAmount,
        InitiationDate = e.InitiationDate,
        InvoiceId = e.InvoiceId,
        MatchedOrderId = e.MatchedOrderId
    };

    private static DateTimeOffset ParseIso(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new PaymentValidationException($"'{field}' must be an ISO-8601 date-time.");
        }
        return parsed;
    }
}

public class ReconciliationRequest : BaseRequest
{
    public string? From { get; set; }
    public string? To { get; set; }
}

public class ReconciliationEntryDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal? FeeAmount { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public string? InvoiceId { get; set; }
    public int? MatchedOrderId { get; set; }
}

public class UnreconciledOrderDto
{
    public int OrderId { get; set; }
    public string InvoiceId { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public int PayPalTransactionCount { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> InPayPalNotInEShop { get; set; } = new();
    public List<UnreconciledOrderDto> InEShopNotInPayPal { get; set; } = new();
    public string Note { get; set; } = string.Empty;
}
