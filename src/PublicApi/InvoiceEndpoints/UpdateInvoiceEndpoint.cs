using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// PATCH /api/invoices/{invoiceId} — correct the due date or customer details of one of the
/// shopper's own bills, while it has not yet been put to the shopper and has not been withdrawn.
/// The billed amount is not correctable here.
/// </summary>
public class UpdateInvoiceEndpoint : IEndpoint<IResult, CorrectInvoiceRequest, IInvoiceAppService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, CorrectInvoiceRequest request, IInvoiceAppService appService, ClaimsPrincipal user) =>
            {
                request.InvoiceId = invoiceId;
                return await HandleAsync(request, appService, user);
            })
            .Produces<InvoiceDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(CorrectInvoiceRequest request, IInvoiceAppService appService, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var result = await appService.CorrectInvoiceAsync(buyerId, request);
        return result.ToHttpResult();
    }
}
