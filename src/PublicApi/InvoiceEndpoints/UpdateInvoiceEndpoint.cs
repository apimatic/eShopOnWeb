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
/// Corrects the due date / customer details on one of the caller's own bills, while it is still a
/// draft. Once the bill has been put to the shopper or withdrawn, correcting it is refused with a
/// clear error rather than silently doing nothing. Shopper-scoped.
/// </summary>
public class UpdateInvoiceEndpoint : IEndpoint<IResult, UpdateInvoiceRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapMethods("api/invoices/{invoiceId}", new[] { "PATCH" },
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, UpdateInvoiceRequest request, HttpContext http, IInvoiceService invoiceService) =>
            {
                var buyerId = http.User.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                request.InvoiceId = invoiceId;
                request.BuyerId = buyerId;
                return await HandleAsync(request, invoiceService);
            })
            .Produces<InvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(UpdateInvoiceRequest request, IInvoiceService invoiceService)
    {
        var correction = new InvoiceCorrection
        {
            DueDate = request.DueDate,
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail
        };

        var detail = await invoiceService.CorrectInvoiceAsync(request.InvoiceId, request.BuyerId, correction);
        return Results.Ok(InvoiceResponse.From(detail, request.CorrelationId()));
    }
}
