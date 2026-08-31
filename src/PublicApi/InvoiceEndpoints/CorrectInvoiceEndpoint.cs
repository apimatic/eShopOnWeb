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
/// Corrects the due date or customer details on a bill that has not yet been put to the shopper. Once the
/// bill has been issued or withdrawn the caller is told it can no longer be corrected, rather than the
/// change silently doing nothing. Shopper-scoped, with operators able to correct any bill.
/// </summary>
public class CorrectInvoiceEndpoint : IEndpoint<IResult, CorrectInvoiceRequest, ClaimsPrincipal, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapMethods("api/invoices/{invoiceId}", new[] { "PATCH" },
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, CorrectInvoiceRequest request, ClaimsPrincipal user, IInvoiceService invoiceService) =>
            {
                request.InvoiceId = invoiceId;
                return await HandleAsync(request, user, invoiceService);
            })
            .Produces<InvoiceDetailsResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(CorrectInvoiceRequest request, ClaimsPrincipal user, IInvoiceService invoiceService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var isOperator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        var view = await invoiceService.CorrectInvoiceAsync(
            request.InvoiceId, buyerId, isOperator, request.DueDate, request.CustomerName, request.CustomerEmail);

        return Results.Ok(InvoiceDetailsResponse.From(view, request.CorrelationId()));
    }
}
