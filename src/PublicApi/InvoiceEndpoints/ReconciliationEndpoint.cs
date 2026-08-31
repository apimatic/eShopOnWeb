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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Lists the provider's own record of bills raised in a date range and lines them up against what eShop
/// believes it raised, so a bill the provider knows about and eShop doesn't — or the reverse — is
/// visible, and bills that are not this application's are plainly marked as such. Operator action.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, string, string, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IInvoiceService invoiceService) =>
                await HandleAsync(from, to, invoiceService))
            .Produces<ReconciliationResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(string from, string to, IInvoiceService invoiceService)
    {
        if (!TryParseIso(from, out var fromDate))
        {
            return Results.BadRequest("'from' must be an ISO-8601 date-time (e.g. 2026-08-01T00:00:00Z).");
        }

        if (!TryParseIso(to, out var toDate))
        {
            return Results.BadRequest("'to' must be an ISO-8601 date-time (e.g. 2026-08-31T23:59:59Z).");
        }

        if (toDate < fromDate)
        {
            return Results.BadRequest("'to' must not be before 'from'.");
        }

        var report = await invoiceService.ReconcileAsync(fromDate, toDate);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            ProviderInvoiceCount = report.ProviderInvoiceCount,
            EShopInvoiceCount = report.EShopInvoiceCount,
            Entries = report.Entries.Select(entry => new ReconciliationEntryDto
            {
                InvoiceId = entry.InvoiceId,
                InvoiceNumber = entry.InvoiceNumber,
                IsEShopInvoice = entry.IsEShopInvoice,
                Category = entry.Category.ToString(),
                ProviderStatus = entry.ProviderStatus,
                LocalStatus = entry.LocalStatus,
                OrderId = entry.OrderId,
                BuyerId = entry.BuyerId,
                Amount = entry.Amount,
                Currency = entry.Currency,
                CreatedDate = entry.CreatedDate
            }).ToList()
        };

        return Results.Ok(response);
    }

    private static bool TryParseIso(string value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>How many bills the provider itself recorded in the range (including bills that are not eShop's).</summary>
    public int ProviderInvoiceCount { get; set; }

    /// <summary>How many of the reconciled bills are eShop's.</summary>
    public int EShopInvoiceCount { get; set; }

    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string InvoiceId { get; set; } = string.Empty;
    public string? InvoiceNumber { get; set; }

    /// <summary>Whether this bill is eShop's or belongs to other activity on the shared provider account.</summary>
    public bool IsEShopInvoice { get; set; }

    /// <summary>Matched, MissingFromEShop, MissingFromProvider, or ForeignToEShop.</summary>
    public string Category { get; set; } = string.Empty;

    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public int? OrderId { get; set; }
    public string? BuyerId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }
}
