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
/// Reports a bill's current state — whatever the provider reports about how it reached that state, and,
/// once it has been put to the shopper, how they can pay it (the top-level <c>paymentLink</c>). Scoped
/// to the caller's own bills unless the caller is an operator.
/// </summary>
public class GetInvoiceEndpoint : IEndpoint
{
    private readonly IInvoiceManagementService _invoiceService;

    public GetInvoiceEndpoint(IInvoiceManagementService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, HttpContext context) =>
            {
                return await HandleAsync(invoiceId, context.User);
            })
            .Produces<InvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(string invoiceId, ClaimsPrincipal user)
    {
        var callerId = user.GetCallerId();
        if (string.IsNullOrEmpty(callerId))
        {
            return Results.Unauthorized();
        }

        var snapshot = await _invoiceService.GetInvoiceAsync(invoiceId, callerId, user.IsOperator());
        return Results.Ok(InvoiceResponse.From(snapshot));
    }
}
