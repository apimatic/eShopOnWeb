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
/// Corrects the due date or customer details of one of the caller's own bills, while it is still a draft.
/// What is billed comes from the order, so the amount is not correctable here. Once the bill has been put to
/// the shopper or withdrawn, correcting it is refused with a conflict rather than silently doing nothing.
/// </summary>
public class UpdateInvoiceEndpoint : IEndpoint<IResult, UpdateInvoiceRequest>
{
    private readonly IInvoiceService _invoiceService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UpdateInvoiceEndpoint(IInvoiceService invoiceService, IHttpContextAccessor httpContextAccessor)
    {
        _invoiceService = invoiceService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, UpdateInvoiceRequest request) =>
            {
                request.InvoiceId = invoiceId;
                return await HandleAsync(request);
            })
            .Produces<UpdateInvoiceResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(UpdateInvoiceRequest request)
    {
        var context = _httpContextAccessor.HttpContext!;
        var buyerId = context.User?.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var customer = request.Customer is null
            ? null
            : new InvoiceCustomerDetails(request.Customer.Name, request.Customer.Email);

        // A refusal on a non-draft bill surfaces as InvoiceNotModifiableException → mapped to 409 by the middleware.
        var invoice = await _invoiceService.CorrectDraftInvoiceAsync(
            request.InvoiceId, buyerId, request.DueDate, customer, context.RequestAborted);

        if (invoice is null)
            return Results.NotFound();

        var response = new UpdateInvoiceResponse(request.CorrelationId())
        {
            Invoice = InvoiceDto.From(invoice),
        };
        return Results.Ok(response);
    }
}
