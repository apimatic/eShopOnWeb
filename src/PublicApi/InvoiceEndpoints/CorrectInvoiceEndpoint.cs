using System;
using System.Security.Claims;
using System.Threading;
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
/// Corrects the due date and/or customer details of a bill that has not yet been put to the shopper.
/// The billed amount is not correctable here (it comes from the order). Once the bill has been put to
/// the shopper or withdrawn, correcting it returns a 409 rather than silently doing nothing.
/// </summary>
public class CorrectInvoiceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapMethods("api/invoices/{invoiceId}", new[] { "PATCH" },
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, CorrectInvoiceRequest request, IInvoicingService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                await HandleAsync(invoiceId, request, service, user, cancellationToken))
            .Produces<InvoiceDetailsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Invoices");
    }

    public async Task<IResult> HandleAsync(string invoiceId, CorrectInvoiceRequest request, IInvoicingService service, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var caller = InvoiceApiHelpers.GetCaller(user);
        var correction = new InvoiceCorrection(request.DueDate, request.CustomerName, request.CustomerEmail);
        var result = await service.CorrectInvoiceAsync(invoiceId, correction, caller, cancellationToken);
        if (!result.IsSuccess)
        {
            return InvoiceApiHelpers.ToFailure(result);
        }
        return Results.Ok(InvoiceDetailsResponse.From(result.Value!, request.CorrelationId()));
    }
}

/// <summary>
/// Request body for correcting a bill. Any field left null is kept as-is. The billed amount is
/// intentionally absent — it always comes from the order.
/// </summary>
public class CorrectInvoiceRequest : BaseRequest
{
    public DateOnly? DueDate { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
}
