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
/// Operator action: withdraws a bill that should not be paid. Afterwards it is no longer payable and
/// no payment link is handed out. Restricted to the administrator role.
/// </summary>
public class WithdrawInvoiceEndpoint : IEndpoint<IResult, InvoiceActionRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/withdraw",
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
        var result = await invoiceService.WithdrawInvoiceAsync(request.InvoiceId, ct);

        return result.Outcome switch
        {
            ServiceOutcome.Ok => Results.Ok(InvoiceResponse.From(result.Value!, request.CorrelationId())),
            _ => Results.NotFound(result.Error)
        };
    }
}
