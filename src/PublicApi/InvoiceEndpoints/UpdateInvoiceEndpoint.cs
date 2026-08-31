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
/// Corrects the due date or customer details of a bill that has not yet been put to the shopper. The
/// amount is not correctable here — it always comes from the order. Once the bill has been issued or
/// withdrawn the correction is refused (409) rather than silently doing nothing.
/// </summary>
public class UpdateInvoiceEndpoint : IEndpoint
{
    private readonly IInvoiceManagementService _invoiceService;

    public UpdateInvoiceEndpoint(IInvoiceManagementService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, CorrectInvoiceRequest request, HttpContext context) =>
            {
                return await HandleAsync(invoiceId, request, context.User);
            })
            .Produces<InvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(string invoiceId, CorrectInvoiceRequest request, ClaimsPrincipal user)
    {
        var callerId = user.GetCallerId();
        if (string.IsNullOrEmpty(callerId))
        {
            return Results.Unauthorized();
        }

        var customer = ToVisaCustomer(request.Customer);
        var snapshot = await _invoiceService.CorrectInvoiceAsync(invoiceId, request.DueDate, customer, callerId, user.IsOperator());
        return Results.Ok(InvoiceResponse.From(snapshot));
    }

    private static VisaCustomer? ToVisaCustomer(CustomerDto? dto)
    {
        if (dto is null || (string.IsNullOrWhiteSpace(dto.Name) && string.IsNullOrWhiteSpace(dto.Email)))
        {
            return null;
        }

        return new VisaCustomer(dto.Name ?? string.Empty, dto.Email ?? string.Empty);
    }
}
