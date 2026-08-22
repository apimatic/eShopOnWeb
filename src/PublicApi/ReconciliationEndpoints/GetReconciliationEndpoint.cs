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

public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IPaymentService payments) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, payments);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService payments)
    {
        if (!DateTimeOffset.TryParse(request.From, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var from))
        {
            throw new InvalidPaymentRequestException("`from` must be an ISO-8601 date-time.");
        }

        if (!DateTimeOffset.TryParse(request.To, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var to))
        {
            throw new InvalidPaymentRequestException("`to` must be an ISO-8601 date-time.");
        }

        var report = await payments.ReconcileAsync(from, to);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(ReconciliationRowResponse.From).ToList(),
            PayPalOnly = report.PayPalOnly.Select(ReconciliationRowResponse.From).ToList(),
            EshopOnly = report.EshopOnly.Select(ReconciliationRowResponse.From).ToList()
        });
    }
}

public class ReconciliationRequest : BaseRequest
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<ReconciliationRowResponse> Matched { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationRowResponse> PayPalOnly { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationRowResponse> EshopOnly { get; set; } = new();
}

public class ReconciliationRowResponse
{
    public string? PayPalTransactionId { get; set; }
    public string? PayPalInvoiceId { get; set; }
    public string? PayPalCustomField { get; set; }
    public string? PayPalStatus { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? PayPalCurrency { get; set; }
    public DateTimeOffset? PayPalTime { get; set; }
    public int? OrderId { get; set; }
    public string? OrderStatus { get; set; }
    public string? CaptureId { get; set; }
    public string? AuthorizationId { get; set; }
    public string MatchReason { get; set; } = string.Empty;

    public static ReconciliationRowResponse From(ReconciliationRow row) => new()
    {
        PayPalTransactionId = row.PayPalTransactionId,
        PayPalInvoiceId = row.PayPalInvoiceId,
        PayPalCustomField = row.PayPalCustomField,
        PayPalStatus = row.PayPalStatus,
        PayPalAmount = row.PayPalAmount,
        PayPalCurrency = row.PayPalCurrency,
        PayPalTime = row.PayPalTime,
        OrderId = row.OrderId,
        OrderStatus = row.OrderStatus?.ToString(),
        CaptureId = row.CaptureId,
        AuthorizationId = row.AuthorizationId,
        MatchReason = row.MatchReason
    };
}
