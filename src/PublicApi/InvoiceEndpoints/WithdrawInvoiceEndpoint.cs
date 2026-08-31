using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// POST /api/invoices/{invoiceId}/withdraw — operator action to withdraw a bill that should not
/// be paid. Afterwards it is no longer payable and no way to pay it is handed out.
/// </summary>
public class WithdrawInvoiceEndpoint : IEndpoint<IResult, string, IInvoiceAppService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/withdraw",
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
        var result = await appService.WithdrawInvoiceAsync(invoiceId);
        return result.ToHttpResult();
    }
}
