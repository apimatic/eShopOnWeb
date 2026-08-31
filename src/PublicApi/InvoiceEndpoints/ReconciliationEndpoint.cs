using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
/// Operator action: lists the provider's own record of bills raised in a date range and lines them up
/// against what eShop believes it raised, making plain which bills are eShop's and which are foreign to
/// the shared account. <c>from</c> and <c>to</c> are ISO-8601 date-times. Restricted to the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IInvoicingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IInvoicingService service, CancellationToken ct) =>
            {
                if (from is null || to is null)
                    return Results.BadRequest(new { error = "Both 'from' and 'to' ISO-8601 date-times are required." });
                if (from > to)
                    return Results.BadRequest(new { error = "'from' must not be after 'to'." });

                return await HandleAsync(new ReconciliationRequest(from.Value, to.Value), service, ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IInvoicingService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IInvoicingService service, CancellationToken ct)
    {
        return await InvoicingProblem.GuardAsync(async () =>
        {
            var report = await service.ReconcileAsync(request.From, request.To, ct);
            var response = new ReconciliationResponse(request.CorrelationId())
            {
                From = report.From,
                To = report.To,
                ProviderInvoiceCount = report.ProviderInvoiceCount,
                EShopInvoiceCount = report.EShopInvoiceCount,
                MatchedCount = report.MatchedCount,
                Note = report.Note,
                Entries = report.Entries.Select(InvoiceViewMapper.ToView).ToList(),
            };
            return Results.Ok(response);
        });
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }

    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
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

    /// <summary>Number of provider records that fell in the range.</summary>
    public int ProviderInvoiceCount { get; set; }

    /// <summary>Number of eShop-tracked bills that fell in the range.</summary>
    public int EShopInvoiceCount { get; set; }

    /// <summary>Number matched on both sides.</summary>
    public int MatchedCount { get; set; }

    /// <summary>How to read the report, given the provider list's limitations.</summary>
    public string Note { get; set; } = string.Empty;

    public List<ReconciliationEntryView> Entries { get; set; } = new();
}
