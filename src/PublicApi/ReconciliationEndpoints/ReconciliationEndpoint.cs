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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator report lining up PayPal's own record of transactions against eShop orders for a date range, so a
/// payment one side knows about and the other does not is visible. Covers the whole range, not just one page.
/// GET /api/reconciliation?from={iso8601}&amp;to={iso8601}
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service)
    {
        var from = ParseDate(request.From, nameof(request.From));
        var to = ParseDate(request.To, nameof(request.To));
        if (to < from)
            throw new PaymentValidationException("'to' must be on or after 'from'.");

        var report = await service.BuildAsync(from, to);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            EShopCaptureCount = report.EShopCaptureCount,
            MatchedCount = report.MatchedCount,
            InPayPalOnlyCount = report.InPayPalOnlyCount,
            InEShopOnlyCount = report.InEShopOnlyCount,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                Status = e.Status.ToString(),
                PayPalTransactionId = e.PayPalTransactionId,
                PayPalTransactionStatus = e.PayPalTransactionStatus,
                PayPalAmount = e.PayPalAmount,
                OrderId = e.OrderId,
                EShopCaptureId = e.EShopCaptureId,
                EShopCapturedAmount = e.EShopCapturedAmount,
                Currency = e.Currency,
                Date = e.Date,
                AmountsAgree = e.AmountsAgree
            }).ToList()
        };
        return Results.Ok(response);
    }

    private static DateTimeOffset ParseDate(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new PaymentValidationException($"'{field.ToLowerInvariant()}' is required (ISO-8601 date-time).");
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            throw new PaymentValidationException($"'{field.ToLowerInvariant()}' is not a valid ISO-8601 date-time.");
        return dt;
    }
}

public class ReconciliationRequest : BaseRequest
{
    public string? From { get; set; }
    public string? To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int EShopCaptureCount { get; set; }
    public int MatchedCount { get; set; }
    public int InPayPalOnlyCount { get; set; }
    public int InEShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string Status { get; set; } = string.Empty;
    public string? PayPalTransactionId { get; set; }
    public string? PayPalTransactionStatus { get; set; }
    public decimal? PayPalAmount { get; set; }
    public int? OrderId { get; set; }
    public string? EShopCaptureId { get; set; }
    public decimal? EShopCapturedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? Date { get; set; }
    public bool AmountsAgree { get; set; }
}
