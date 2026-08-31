using System;
using System.Security.Claims;
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
/// Corrects the due date or the customer details a bill carries, while it is still a draft. Once
/// the bill has been put to the shopper or withdrawn, the caller is told the correction is refused
/// rather than it silently doing nothing.
/// </summary>
public class UpdateInvoiceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPatch("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string invoiceId,
                UpdateInvoiceRequest request,
                ClaimsPrincipal user,
                IInvoiceService invoiceService) =>
            {
                if (request is null ||
                    (request.DueDate is null && request.CustomerName is null && request.CustomerEmail is null))
                {
                    return Results.BadRequest("Provide a new due date and/or customer details to correct.");
                }

                if (request.DueDate is { } dueDate && dueDate < DateOnly.FromDateTime(DateTime.UtcNow.Date))
                {
                    return Results.BadRequest("The due date must be today or in the future.");
                }

                var view = await invoiceService.CorrectInvoiceAsync(
                    invoiceId, user.GetBuyerId(), user.IsOperator(),
                    request.DueDate, request.CustomerName, request.CustomerEmail);

                return Results.Ok(InvoiceResponse.From(view));
            })
            .Produces<InvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }
}
