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
/// Puts the bill to the shopper. Afterwards the application can hand out a way to pay it,
/// and the bill reports itself as having been put to them. Operator action.
/// </summary>
public class IssueInvoiceEndpoint : IEndpoint<IResult, InvoiceActionRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/issue",
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
        var result = await invoiceService.IssueAsync(request.InvoiceId);

        return ApiResults.From(result, view => Results.Ok(new InvoiceActionResponse(request.CorrelationId())
        {
            Invoice = InvoiceDtoMapper.ToDto(view),
            PaymentLink = view.PaymentLink
        }));
    }
}
