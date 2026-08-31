using System;
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
/// Corrects the due date and/or the customer details a bill carries. The billed amount comes from the
/// order and is not correctable here. Any field left null is left unchanged.
/// </summary>
public class UpdateInvoiceRequest : BaseRequest
{
    /// <summary>New calendar due date, or null to leave it unchanged.</summary>
    public DateOnly? DueDate { get; set; }

    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
}

/// <summary>
/// Corrects one of the caller's draft bills. Once the bill has been put to the shopper or withdrawn,
/// correcting it is refused (409) rather than silently doing nothing.
/// </summary>
public class UpdateInvoiceEndpoint : InvoiceEndpointBase, IEndpoint
{
    public UpdateInvoiceEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapMethods("api/invoices/{invoiceId}", new[] { "PATCH" },
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, UpdateInvoiceRequest request, IInvoicingService invoicingService) =>
            {
                var buyerId = CurrentUserName;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                DateTimeOffset? dueDate = request.DueDate.HasValue
                    ? new DateTimeOffset(request.DueDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                    : null;

                var details = await invoicingService.CorrectInvoiceAsync(invoiceId, buyerId, dueDate,
                    request.CustomerName, request.CustomerEmail, RequestAborted);
                return Results.Ok(details);
            })
            .Produces<InvoiceDetails>()
            .WithTags("InvoiceEndpoints");
    }
}
