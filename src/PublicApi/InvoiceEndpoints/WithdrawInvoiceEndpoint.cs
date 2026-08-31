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
/// Operator action: withdraws a bill that should not be paid. Afterwards it is no longer payable and
/// the way to pay it is no longer handed out. Restricted to administrators.
/// </summary>
public class WithdrawInvoiceEndpoint : IEndpoint<IResult, InvoiceActionRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/withdraw",
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
        var invoice = await invoiceService.WithdrawInvoiceAsync(request.InvoiceId);
        return Results.Ok(new InvoiceActionResponse(request.CorrelationId())
        {
            InvoiceId = invoice.Id,
            Status = invoice.Status.ToString()
        });
    }
}
