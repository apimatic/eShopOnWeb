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
/// Withdraws a bill that should not be paid. Afterwards it is no longer payable and the
/// way to pay it is no longer handed out. Operator action.
/// </summary>
public class WithdrawInvoiceEndpoint : IEndpoint<IResult, InvoiceActionRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/withdraw",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoiceService invoiceService) =>
            {
                return await HandleAsync(new InvoiceActionRequest { InvoiceId = invoiceId }, invoiceService);
            })
            .Produces<InvoiceActionResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(InvoiceActionRequest request, IInvoiceService invoiceService)
    {
        var result = await invoiceService.WithdrawAsync(request.InvoiceId);

        return ApiResults.From(result, view => Results.Ok(new InvoiceActionResponse(request.CorrelationId())
        {
            Invoice = InvoiceDtoMapper.ToDto(view),
            PaymentLink = view.PaymentLink
        }));
    }
}
