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
/// Corrects the due date and/or customer details on a still-draft bill the shopper owns. The amount
/// is not correctable — it comes from the order. Once the bill has been put to the shopper or
/// withdrawn, correction is refused with 409 rather than silently doing nothing.
/// </summary>
public class UpdateInvoiceEndpoint : IEndpoint<IResult, UpdateInvoiceRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapMethods("api/invoices/{invoiceId}", new[] { "PATCH" },
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string invoiceId,
                UpdateInvoiceRequest request,
                ClaimsPrincipal user,
                IInvoiceService invoiceService,
                CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.InvoiceId = invoiceId;
                return await ExecuteAsync(request, buyerId, invoiceService, ct);
            })
            .Produces<InvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(UpdateInvoiceRequest request, IInvoiceService invoiceService) =>
        ExecuteAsync(request, string.Empty, invoiceService, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(UpdateInvoiceRequest request, string buyerId,
        IInvoiceService invoiceService, CancellationToken ct)
    {
        DateTimeOffset? dueDate = request.DueDate.HasValue
            ? new DateTimeOffset(request.DueDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;

        var result = await invoiceService.CorrectInvoiceAsync(
            request.InvoiceId, buyerId, dueDate, request.CustomerName, request.CustomerEmail, ct);

        return result.Outcome switch
        {
            ServiceOutcome.Ok => Results.Ok(InvoiceResponse.From(result.Value!, request.CorrelationId())),
            ServiceOutcome.Conflict => Results.Conflict(result.Error),
            _ => Results.NotFound(result.Error)
        };
    }
}
