using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Reads one of the caller's own bills: its current state, whatever the provider reports about how it
/// reached that state, and — once put to the shopper — how they can pay it.
/// </summary>
public class GetInvoiceByIdEndpoint : IEndpoint<IResult, GetInvoiceByIdRequest>
{
    private readonly IInvoiceService _invoiceService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetInvoiceByIdEndpoint(IInvoiceService invoiceService, IHttpContextAccessor httpContextAccessor)
    {
        _invoiceService = invoiceService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId) =>
            {
                return await HandleAsync(new GetInvoiceByIdRequest(invoiceId));
            })
            .Produces<GetInvoiceResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(GetInvoiceByIdRequest request)
    {
        var context = _httpContextAccessor.HttpContext!;
        var buyerId = context.User?.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var details = await _invoiceService.GetInvoiceForBuyerAsync(request.InvoiceId, buyerId, context.RequestAborted);
        if (details is null)
            return Results.NotFound();

        var response = new GetInvoiceResponse(request.CorrelationId())
        {
            Invoice = InvoiceDto.From(details.Invoice),
            PaymentLink = details.Invoice.PayableLink,
            History = details.Provider.History
                .Select(h => new InvoiceHistoryDto { Event = h.Event, Date = h.Date })
                .ToList(),
        };
        return Results.Ok(response);
    }
}
