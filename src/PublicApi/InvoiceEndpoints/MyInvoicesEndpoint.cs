using System;
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
/// Lists the caller's own bills, each showing where it has got to. The caller's identity comes from
/// the token; a shopper only ever sees their own bills.
/// </summary>
public class MyInvoicesEndpoint : IEndpoint<IResult, IInvoiceService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-invoices",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IInvoiceService invoiceService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(invoiceService, user);
            })
            .Produces<MyInvoicesResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(IInvoiceService invoiceService, ClaimsPrincipal user)
    {
        var invoices = await invoiceService.GetInvoicesForShopperAsync(user.GetBuyerId());
        var response = new MyInvoicesResponse(Guid.NewGuid())
        {
            Invoices = invoices.Select(InvoiceDtoMapper.ToSummaryDto).ToList()
        };
        return Results.Ok(response);
    }
}
