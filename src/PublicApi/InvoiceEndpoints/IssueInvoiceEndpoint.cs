using System.Threading;
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
/// Operator action: puts a bill to the shopper. Afterwards the application can hand out a way to pay
/// it and the bill reports itself as issued. Restricted to the administrator role.
/// </summary>
public class IssueInvoiceEndpoint : IEndpoint<IResult, InvoiceActionRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/issue",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string invoiceId,
                IInvoiceService invoiceService,
                CancellationToken ct) =>
            {
                return await ExecuteAsync(new InvoiceActionRequest(invoiceId), invoiceService, ct);
            })
            .Produces<InvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(InvoiceActionRequest request, IInvoiceService invoiceService) =>
        ExecuteAsync(request, invoiceService, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(InvoiceActionRequest request,
        IInvoiceService invoiceService, CancellationToken ct)
    {
        var result = await invoiceService.IssueInvoiceAsync(request.InvoiceId, ct);

        return result.Outcome switch
        {
            ServiceOutcome.Ok => Results.Ok(InvoiceResponse.From(result.Value!, request.CorrelationId())),
            _ => Results.NotFound(result.Error)
        };
    }
}
