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
/// Puts the bill to the shopper (operator action). Afterwards the application can hand out a way for them
/// to pay it, and the bill reports itself as having been put to them.
/// </summary>
public class IssueInvoiceEndpoint : IEndpoint<IResult, string, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/issue",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoiceService invoiceService) =>
                await HandleAsync(invoiceId, invoiceService))
            .Produces<InvoiceDetailsResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(string invoiceId, IInvoiceService invoiceService)
    {
        var view = await invoiceService.IssueInvoiceAsync(invoiceId);
        return Results.Ok(InvoiceDetailsResponse.From(view));
    }
}
