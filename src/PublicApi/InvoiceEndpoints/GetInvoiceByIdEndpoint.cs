using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Returns a bill's current state, the provider's account of how it reached that state, and — once it
/// has been put to the shopper — how they can pay it (a top-level <c>paymentLink</c>). Scoped to the
/// caller's own bills.
/// </summary>
public class GetInvoiceByIdEndpoint : IEndpoint<IResult, string, HttpContext>
{
    private readonly IInvoiceService _invoiceService;

    public GetInvoiceByIdEndpoint(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, HttpContext http) => await HandleAsync(invoiceId, http))
            .Produces<InvoiceView>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(string invoiceId, HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var view = await _invoiceService.GetForShopperAsync(invoiceId, buyerId);
        return view is null ? Results.NotFound() : Results.Ok(view);
    }
}
