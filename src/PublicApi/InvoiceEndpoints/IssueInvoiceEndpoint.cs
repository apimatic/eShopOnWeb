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
/// Operator action: puts a bill to its shopper. Afterwards the application can hand out a way for the
/// shopper to pay it and the bill reports itself as having been put to them. Restricted to administrators.
/// </summary>
public class IssueInvoiceEndpoint : IEndpoint<IResult, InvoiceActionRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/issue",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int invoiceId, IInvoiceService invoiceService) =>
            {
                return await HandleAsync(new InvoiceActionRequest(invoiceId), invoiceService);
            })
            .Produces<InvoiceActionResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(InvoiceActionRequest request, IInvoiceService invoiceService)
    {
        var invoice = await invoiceService.IssueInvoiceAsync(request.InvoiceId);
        return Results.Ok(new InvoiceActionResponse(request.CorrelationId())
        {
            InvoiceId = invoice.Id,
            Status = invoice.Status.ToString()
        });
    }
}
