using System;
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

public class UpdateInvoiceRequest
{
    /// <summary>New due date. Omit to leave unchanged.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>New customer details. Omit to leave unchanged.</summary>
    public CustomerDto? Customer { get; set; }
}

/// <summary>
/// Corrects the due date or customer details of a bill that has not yet been put to the shopper. What
/// is billed still comes from the order, so the amount is not correctable here. Once the bill has been
/// issued or withdrawn, correcting it is refused with a 409 rather than silently doing nothing.
/// </summary>
public class UpdateInvoiceEndpoint : IEndpoint<IResult, string, UpdateInvoiceRequest, HttpContext>
{
    private readonly IInvoiceService _invoiceService;

    public UpdateInvoiceEndpoint(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, UpdateInvoiceRequest request, HttpContext http) =>
                await HandleAsync(invoiceId, request, http))
            .Produces<InvoiceView>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(string invoiceId, UpdateInvoiceRequest request, HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.DueDate is null && request.Customer is null)
        {
            return Results.BadRequest(new { error = "Provide a new due date and/or customer details to correct." });
        }

        var customer = CreateInvoiceForOrderEndpoint.ToCustomerDetails(request.Customer);

        // An InvoiceStateConflictException (issued/withdrawn bill) is mapped to 409 by the middleware.
        var view = await _invoiceService.CorrectForShopperAsync(invoiceId, buyerId, request.DueDate, customer);
        return view is null ? Results.NotFound() : Results.Ok(view);
    }
}
