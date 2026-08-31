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
/// once it has been put to the shopper — how they can pay it (a top-level <c>paymentLink</c>). A
/// shopper only ever sees their own bill.
/// </summary>
public class GetInvoiceEndpoint : IEndpoint<IResult, InvoiceRef, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, ClaimsPrincipal user, IInvoiceService invoiceService) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new InvoiceRef(invoiceId, buyerId, CallerIdentity.IsOperator(user)), invoiceService);
            })
            .Produces<InvoiceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(InvoiceRef request, IInvoiceService invoiceService)
    {
        var result = await invoiceService.GetAsync(request.BuyerId, request.IsOperator, request.InvoiceId);
        return InvoiceApiResults.ToHttp(result, view => Results.Ok(InvoiceApiResults.ToResponse(view)));
    }
}
