using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// GET /api/my-invoices — the authenticated shopper's own bills, each showing where it has got to.
/// </summary>
public class MyInvoicesEndpoint : IEndpoint<IResult, IInvoiceAppService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-invoices",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IInvoiceAppService appService, ClaimsPrincipal user) =>
                await HandleAsync(appService, user))
            .Produces<IReadOnlyList<InvoiceSummaryDto>>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(IInvoiceAppService appService, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var invoices = await appService.GetMyInvoicesAsync(buyerId);
        return Results.Ok(invoices);
    }
}
