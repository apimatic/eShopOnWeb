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
/// GET /api/invoices/{invoiceId} — the current state of one of the shopper's own bills, the
/// provider's account of how it got there, and — once put to the shopper — how to pay it.
/// </summary>
public class GetInvoiceByIdEndpoint : IEndpoint<IResult, string, IInvoiceAppService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoiceAppService appService, ClaimsPrincipal user) =>
                await HandleAsync(invoiceId, appService, user))
            .Produces<InvoiceDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(string invoiceId, IInvoiceAppService appService, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var result = await appService.GetInvoiceAsync(buyerId, invoiceId);
        return result.ToHttpResult();
    }
}
