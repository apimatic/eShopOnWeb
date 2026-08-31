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
/// Raises a bill with the provider for one of the shopper's orders. What is billed comes from the
/// order itself; the request carries only the due date (and optional customer details). The bill
/// starts as a draft — not yet put to the shopper. Returns the new <c>invoiceId</c>.
/// </summary>
public class RaiseInvoiceEndpoint : IEndpoint<IResult, RaiseInvoiceRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                RaiseInvoiceRequest request,
                ClaimsPrincipal user,
                IInvoiceService invoiceService,
                CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.OrderId = orderId;
                return await ExecuteAsync(request, buyerId, invoiceService, ct);
            })
            .Produces<InvoiceResponse>(StatusCodes.Status201Created)
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(RaiseInvoiceRequest request, IInvoiceService invoiceService) =>
        ExecuteAsync(request, string.Empty, invoiceService, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(RaiseInvoiceRequest request, string buyerId,
        IInvoiceService invoiceService, CancellationToken ct)
    {
        var dueDate = new DateTimeOffset(request.DueDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var result = await invoiceService.RaiseInvoiceAsync(
            request.OrderId, buyerId, dueDate, request.CustomerName, request.CustomerEmail, ct);

        return result.Outcome switch
        {
            ServiceOutcome.Ok => Results.Created(
                $"api/invoices/{result.Value!.InvoiceId}",
                InvoiceResponse.From(result.Value!, request.CorrelationId())),
            ServiceOutcome.NotFound => Results.NotFound(result.Error),
            _ => Results.NotFound(result.Error)
        };
    }
}
