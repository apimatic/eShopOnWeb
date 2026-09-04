using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Integrations.Reconciliation;
using MinimalApi.Endpoint;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator report: PayPal's own record of transactions for a date range, lined up
/// against eShop orders/payments so that a payment PayPal knows about and eShop does
/// not - or the reverse - is visible. Covers the whole range, walking every page of
/// PayPal's paged transaction reporting API.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IPaymentProcessingService paymentProcessing) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), paymentProcessing);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentProcessingService paymentProcessing)
    {
        var response = new ReconciliationResponse(request.CorrelationId());

        if (!TryParseIso8601(request.From, out var from) || !TryParseIso8601(request.To, out var to))
        {
            return Results.BadRequest(new { error = "from and to are required and must be ISO-8601 date-times, e.g. 2026-09-01T00:00:00Z" });
        }

        var report = await paymentProcessing.ReconcileAsync(from, to);

        response.From = report.From;
        response.To = report.To;
        response.GeneratedAt = report.GeneratedAt;
        response.Transactions = report.Transactions;
        response.EshopPayments = report.EshopPayments;
        response.Summary = report.Summary;
        return Results.Ok(response);
    }

    private static bool TryParseIso8601(string? value, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out parsed);
}

public class ReconciliationRequest : BaseRequest
{
    public string? From { get; init; }
    public string? To { get; init; }

    public ReconciliationRequest(string? from, string? to)
    {
        From = from;
        To = to;
    }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public IReadOnlyList<ReconciliationTransactionRow> Transactions { get; set; } = Array.Empty<ReconciliationTransactionRow>();
    public IReadOnlyList<ReconciliationEshopRow> EshopPayments { get; set; } = Array.Empty<ReconciliationEshopRow>();
    public ReconciliationSummary Summary { get; set; } = new();
}
