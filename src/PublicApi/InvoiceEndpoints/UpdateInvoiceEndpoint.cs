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
/// Corrects the due date and/or customer details on one of the caller's own draft bills. Once the
/// bill has been put to the shopper or withdrawn the correction is refused (409) rather than doing
/// nothing. The amount is never correctable here — it comes from the order.
/// </summary>
public class UpdateInvoiceEndpoint : IEndpoint<IResult, UpdateInvoiceRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapMethods("api/invoices/{invoiceId}", new[] { "PATCH" },
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int invoiceId, UpdateInvoiceRequest request, ClaimsPrincipal user, IInvoiceService invoiceService) =>
            {
                request.InvoiceId = invoiceId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, invoiceService);
            })
            .Produces<UpdateInvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(UpdateInvoiceRequest request, IInvoiceService invoiceService)
    {
        var invoice = await invoiceService.CorrectInvoiceAsync(
            request.InvoiceId,
            request.BuyerId,
            request.DueDate,
            request.CustomerName,
            request.CustomerEmail);

        var response = new UpdateInvoiceResponse(request.CorrelationId())
        {
            InvoiceId = invoice.Id,
            Status = invoice.Status.ToString(),
            DueDate = invoice.DueDate,
            CustomerName = invoice.CustomerName,
            CustomerEmail = invoice.CustomerEmail
        };

        return Results.Ok(response);
    }
}
