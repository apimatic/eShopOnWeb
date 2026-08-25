using System;
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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator report: lines up PayPal's own transaction records for a date range against eShop's
/// orders, so a payment either side knows about and the other doesn't is visible. Covers the whole
/// range (paged and chunked internally), not just the first page.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService, CancellationToken ct) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, reconciliationService, ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService reconciliationService,
        CancellationToken ct)
    {
        var response = new ReconciliationResponse(request.CorrelationId());

        var report = await reconciliationService.BuildReportAsync(request.From, request.To, ct);

        response.From = report.From;
        response.To = report.To;
        response.Entries = report.Entries
            .Select(e => new ReconciliationEntryDto(e.OrderId, e.PayPalTransactionId, e.EShopAmount, e.PayPalAmount,
                e.EShopStatus, e.PayPalStatus, e.MatchStatus))
            .ToList();
        response.Warnings = report.Warnings.ToList();
        return Results.Ok(response);
    }
}
