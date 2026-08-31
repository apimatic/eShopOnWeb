using System.Security.Claims;
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
/// Returns a bill's current state, what the provider reports about how it got there, and — once it has
/// been put to the shopper — a top-level <c>paymentLink</c> for how to pay it. A shopper only ever sees
/// their own bill; an operator may see anyone's.
/// </summary>
public class GetInvoiceByIdEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoicingService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                await HandleAsync(invoiceId, service, user, cancellationToken))
            .Produces<InvoiceDetailsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("Invoices");
    }

    public async Task<IResult> HandleAsync(string invoiceId, IInvoicingService service, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var caller = InvoiceApiHelpers.GetCaller(user);
        var result = await service.GetInvoiceAsync(invoiceId, caller, cancellationToken);
        if (!result.IsSuccess)
        {
            return InvoiceApiHelpers.ToFailure(result);
        }
        return Results.Ok(InvoiceDetailsResponse.From(result.Value!, System.Guid.NewGuid()));
    }
}
