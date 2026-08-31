using System;
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
/// Lists the provider's own record of bills raised in a date range and lines them up against what
/// eShop believes it raised, over the whole range, making plain which bills are eShop's and which
/// are not (operator action).
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                DateTimeOffset from,
                DateTimeOffset to,
                IInvoiceService invoiceService) =>
            {
                if (to < from)
                {
                    return Results.BadRequest("'to' must not be earlier than 'from'.");
                }

                var report = await invoiceService.ReconcileAsync(from, to);
                return Results.Ok(ReconciliationResponse.Create(report));
            })
            .Produces<ReconciliationResponse>()
            .WithTags("InvoiceEndpoints");
    }
}
