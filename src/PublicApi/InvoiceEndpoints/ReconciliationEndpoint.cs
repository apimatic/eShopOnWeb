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

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Operator action: lists the provider's own record of bills raised in a date range and lines them up
/// against what eShop believes it raised, making plain which bills are eShop's and which belong to
/// other activity on the shared provider account.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                DateTimeOffset? from,
                DateTimeOffset? to,
                IInvoiceService invoiceService,
                CancellationToken cancellationToken) =>
            {
                if (from is null || to is null)
                {
                    return Results.BadRequest(new { message = "Both 'from' and 'to' ISO-8601 date-times are required." });
                }

                if (to < from)
                {
                    return Results.BadRequest(new { message = "'to' must be the same as or after 'from'." });
                }

                var report = await invoiceService.ReconcileAsync(from.Value, to.Value, cancellationToken);

                var response = new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    TotalCount = report.TotalCount,
                    MatchedCount = report.MatchedCount,
                    ProviderOnlyCount = report.ProviderOnlyCount,
                    EShopOnlyCount = report.EShopOnlyCount,
                    Entries = report.Entries.Select(InvoiceMappings.ToReconciliationDto).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("InvoiceEndpoints");
    }
}
