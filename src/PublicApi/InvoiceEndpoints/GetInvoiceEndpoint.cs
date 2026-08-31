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
/// Reads the current state of one of the caller's own bills: where it stands, how it got there,
/// and — once it has been put to the shopper — how they can pay it (<c>paymentLink</c>).
/// </summary>
public class GetInvoiceEndpoint : IEndpoint<IResult, GetInvoiceRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, HttpContext http, IInvoiceService invoiceService) =>
            {
                var buyerId = http.User.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(new GetInvoiceRequest(invoiceId, buyerId), invoiceService);
            })
            .Produces<InvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(GetInvoiceRequest request, IInvoiceService invoiceService)
    {
        var detail = await invoiceService.GetInvoiceForBuyerAsync(request.InvoiceId, request.BuyerId);
        return Results.Ok(InvoiceResponse.From(detail, request.CorrelationId()));
    }
}
