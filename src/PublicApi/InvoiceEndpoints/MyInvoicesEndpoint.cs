using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
/// The caller's own bills, each showing where it has got to. Each entry carries its <c>invoiceId</c>,
/// which is what the operator endpoints act on.
/// </summary>
public class MyInvoicesEndpoint : IEndpoint
{
    private readonly IInvoiceManagementService _invoiceService;

    public MyInvoicesEndpoint(IInvoiceManagementService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-invoices",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context) =>
            {
                return await HandleAsync(context.User);
            })
            .Produces<IEnumerable<InvoiceSummaryResponse>>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var callerId = user.GetCallerId();
        if (string.IsNullOrEmpty(callerId))
        {
            return Results.Unauthorized();
        }

        var invoices = await _invoiceService.GetInvoicesForBuyerAsync(callerId);
        var response = invoices.Select(InvoiceSummaryResponse.From).ToList();
        return Results.Ok(response);
    }
}
