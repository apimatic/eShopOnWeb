using System;
using System.Collections.Generic;
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

/// <summary>One reconciled bill, lining up the provider's record against eShop's.</summary>
public class ReconciliationEntryDto
{
    /// <summary>eShop's own id for the bill, when eShop raised it. This is what operator endpoints act on.</summary>
    public int? InvoiceId { get; set; }

    /// <summary>The provider's id for the bill.</summary>
    public string ProviderInvoiceId { get; set; } = string.Empty;

    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>Matched, ProviderOnly (not eShop's) or EShopOnly (missing at the provider).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>True when this bill is one eShop believes it raised; false when it is another activity's.</summary>
    public bool IsEShopInvoice { get; set; }

    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public int? OrderId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? CustomerName { get; set; }
    public DateTimeOffset? RaisedAt { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconciliationResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>Count of bills the provider raised in range (across all activity, not just eShop's).</summary>
    public int ProviderCount { get; set; }

    /// <summary>Count of bills eShop believes it raised in range.</summary>
    public int EShopCount { get; set; }

    public int MatchedCount { get; set; }

    /// <summary>Bills the provider knows about that are not this application's.</summary>
    public int ProviderOnlyCount { get; set; }

    /// <summary>Bills eShop believes it raised that the provider's record does not show in range.</summary>
    public int EShopOnlyCount { get; set; }

    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// Operator action: reports the provider's own record of bills raised in a date range and lines it up
/// against what eShop believes it raised, so a bill known to only one side is plainly visible. The
/// provider account carries bills that are not this application's; the report marks which is which.
/// Restricted to administrators.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationEndpoint.ReconciliationRequest, IInvoiceService>
{
    public record ReconciliationRequest(DateTimeOffset From, DateTimeOffset To);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IInvoiceService invoiceService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), invoiceService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IInvoiceService invoiceService)
    {
        var report = await invoiceService.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse(Guid.NewGuid())
        {
            From = report.From,
            To = report.To,
            ProviderCount = report.ProviderCount,
            EShopCount = report.EShopCount,
            MatchedCount = report.MatchedCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            EShopOnlyCount = report.EShopOnlyCount,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                InvoiceId = e.EShopInvoiceId,
                ProviderInvoiceId = e.ProviderInvoiceId,
                InvoiceNumber = e.InvoiceNumber,
                Source = e.Source.ToString(),
                IsEShopInvoice = e.IsEShopInvoice,
                ProviderStatus = e.ProviderStatus,
                EShopStatus = e.EShopStatus?.ToString(),
                OrderId = e.OrderId,
                Amount = e.Amount,
                Currency = e.CurrencyCode,
                CustomerName = e.CustomerName,
                RaisedAt = e.RaisedAt
            }).ToList()
        };

        return Results.Ok(response);
    }
}
