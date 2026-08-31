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
/// Raises a bill with the provider for one of the caller's own orders. What is billed comes from the order
/// itself; the request carries only the due date and optional customer details. The bill starts out a draft.
/// </summary>
public class RaiseInvoiceEndpoint : IEndpoint<IResult, RaiseInvoiceRequest>
{
    private readonly IInvoiceService _invoiceService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RaiseInvoiceEndpoint(IInvoiceService invoiceService, IHttpContextAccessor httpContextAccessor)
    {
        _invoiceService = invoiceService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RaiseInvoiceRequest request) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request);
            })
            .Produces<RaiseInvoiceResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(RaiseInvoiceRequest request)
    {
        var context = _httpContextAccessor.HttpContext!;
        var buyerId = context.User?.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var customer = request.Customer is null
            ? null
            : new InvoiceCustomerDetails(request.Customer.Name, request.Customer.Email);

        var invoice = await _invoiceService.RaiseInvoiceForOrderAsync(
            request.OrderId, buyerId, request.DueDate, customer, context.RequestAborted);

        if (invoice is null)
            return Results.NotFound();

        var response = new RaiseInvoiceResponse(request.CorrelationId())
        {
            InvoiceId = invoice.ProviderInvoiceId,
            Invoice = InvoiceDto.From(invoice),
        };
        return Results.Created($"api/invoices/{invoice.ProviderInvoiceId}", response);
    }
}
