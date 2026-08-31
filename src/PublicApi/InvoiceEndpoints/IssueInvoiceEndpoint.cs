using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// POST /api/invoices/{invoiceId}/issue — operator action to put a bill to the shopper.
/// Afterwards a way to pay it can be handed out and the bill reports itself as issued.
/// </summary>
public class IssueInvoiceEndpoint : IEndpoint<IResult, string, IInvoiceAppService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/issue",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, IInvoiceAppService appService) =>
                await HandleAsync(invoiceId, appService))
            .Produces<InvoiceDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(string invoiceId, IInvoiceAppService appService)
    {
        var result = await appService.IssueInvoiceAsync(invoiceId);
        return result.ToHttpResult();
    }
}
