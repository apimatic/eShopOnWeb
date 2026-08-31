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
/// Corrects the due date or customer details on one of the caller's bills, while the bill
/// has not yet been put to the shopper. Once issued or withdrawn, the caller is told the
/// correction is no longer possible rather than it silently doing nothing.
/// </summary>
public class CorrectInvoiceEndpoint : IEndpoint<IResult, CorrectInvoiceRequest, IInvoiceService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrectInvoiceEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, CorrectInvoiceRequest request, IInvoiceService invoiceService) =>
            {
                request.InvoiceId = invoiceId;
                return await HandleAsync(request, invoiceService);
            })
            .Produces<CorrectInvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(CorrectInvoiceRequest request, IInvoiceService invoiceService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var result = await invoiceService.CorrectInvoiceAsync(
            request.InvoiceId, buyerId, request.DueDate, request.CustomerName, request.CustomerEmail);

        return ApiResults.From(result, view => Results.Ok(new CorrectInvoiceResponse(request.CorrelationId())
        {
            Invoice = InvoiceDtoMapper.ToDto(view)
        }));
    }
}
