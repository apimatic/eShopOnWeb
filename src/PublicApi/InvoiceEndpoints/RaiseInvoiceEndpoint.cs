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
/// Raises a bill with the provider for one of the caller's orders. What is billed comes from the
/// order; the request carries only the due date. The new bill starts out not yet put to the shopper.
/// </summary>
public class RaiseInvoiceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                RaiseInvoiceRequest request,
                ClaimsPrincipal user,
                IInvoiceService invoiceService) =>
            {
                if (request is null || request.DueDate == default)
                {
                    return Results.BadRequest("A due date is required.");
                }

                if (request.DueDate < DateOnly.FromDateTime(DateTime.UtcNow.Date))
                {
                    return Results.BadRequest("The due date must be today or in the future.");
                }

                var view = await invoiceService.RaiseInvoiceForOrderAsync(
                    orderId, user.GetBuyerId(), user.IsOperator(), request.DueDate);

                return Results.Created($"api/invoices/{view.InvoiceId}", InvoiceResponse.From(view));
            })
            .Produces<InvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }
}
