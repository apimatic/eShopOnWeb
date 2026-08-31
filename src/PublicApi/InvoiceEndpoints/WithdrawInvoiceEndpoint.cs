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
/// Withdraws a bill that should not be paid (operator action). Afterwards it is no longer payable and
/// the way to pay it is no longer handed out.
/// </summary>
public class WithdrawInvoiceEndpoint : IEndpoint<IResult, InvoiceRef, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/invoices/{invoiceId}/withdraw",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, ClaimsPrincipal user, IInvoiceService invoiceService) =>
            {
                return await HandleAsync(new InvoiceRef(invoiceId, CallerIdentity.BuyerId(user) ?? string.Empty, true), invoiceService);
            })
            .Produces<InvoiceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(InvoiceRef request, IInvoiceService invoiceService)
    {
        var result = await invoiceService.WithdrawAsync(request.BuyerId, request.IsOperator, request.InvoiceId);
        return InvoiceApiResults.ToHttp(result, view => Results.Ok(InvoiceApiResults.ToResponse(view)));
    }
}
