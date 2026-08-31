using System.Security.Claims;
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
/// Corrects the due date or the customer details a bill carries, while it has not yet been put to
/// the shopper. The amount is not correctable here — it comes from the order. Once the bill has been
/// issued or withdrawn, correcting it returns a conflict rather than silently doing nothing.
/// </summary>
public class UpdateInvoiceEndpoint : IEndpoint<IResult, CorrectInvoiceRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, CorrectInvoiceBody body, ClaimsPrincipal user, IInvoiceService invoiceService) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var customer = body is null || (body.CustomerName is null && body.CustomerEmail is null)
                    ? null
                    : new CustomerDetails(body.CustomerName, body.CustomerEmail);
                var dueDate = body?.DueDate;

                var request = new CorrectInvoiceRequest(invoiceId, dueDate, customer, buyerId, CallerIdentity.IsOperator(user));
                return await HandleAsync(request, invoiceService);
            })
            .Produces<InvoiceResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(CorrectInvoiceRequest request, IInvoiceService invoiceService)
    {
        var result = await invoiceService.CorrectAsync(
            request.BuyerId, request.IsOperator, request.InvoiceId, request.DueDate, request.Customer);
        return InvoiceApiResults.ToHttp(result, view => Results.Ok(InvoiceApiResults.ToResponse(view)));
    }
}
