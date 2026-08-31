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
/// Reads a bill's current state, whatever the provider reports about how it reached that state, and —
/// once it has been put to the shopper — how they can pay it. Shopper-scoped: a shopper sees only their
/// own bill, while an operator may read any.
/// </summary>
public class GetInvoiceByIdEndpoint : IEndpoint<IResult, string, ClaimsPrincipal, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, ClaimsPrincipal user, IInvoiceService invoiceService) =>
                await HandleAsync(invoiceId, user, invoiceService))
            .Produces<InvoiceDetailsResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(string invoiceId, ClaimsPrincipal user, IInvoiceService invoiceService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var isOperator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        var view = await invoiceService.GetInvoiceAsync(invoiceId, buyerId, isOperator);
        return Results.Ok(InvoiceDetailsResponse.From(view));
    }
}
