using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Raises a bill with the provider for an order. What is billed comes from the order itself; the body only
/// carries the calendar date the bill falls due and, optionally, the customer details the bill should carry.
/// </summary>
public class RaiseInvoiceRequest : BaseRequest
{
    /// <summary>The calendar date the bill falls due.</summary>
    public DateOnly DueDate { get; set; }

    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
}

public class RaiseInvoiceResponse : BaseResponse
{
    public RaiseInvoiceResponse(Guid correlationId) : base(correlationId) { }

    public string InvoiceId { get; set; } = string.Empty;
}

/// <summary>
/// Raises a bill for one of the caller's orders, held in draft (not yet put to the shopper). Returns the
/// new bill's provider identifier.
/// </summary>
public class RaiseInvoiceEndpoint : InvoiceEndpointBase, IEndpoint
{
    public RaiseInvoiceEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RaiseInvoiceRequest request, IInvoicingService invoicingService) =>
            {
                var buyerId = CurrentUserName;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var dueDate = new DateTimeOffset(request.DueDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                var invoiceId = await invoicingService.RaiseInvoiceAsync(orderId, buyerId, dueDate,
                    request.CustomerName, request.CustomerEmail, RequestAborted);

                var response = new RaiseInvoiceResponse(request.CorrelationId()) { InvoiceId = invoiceId };
                return Results.Created($"api/invoices/{invoiceId}", response);
            })
            .Produces<RaiseInvoiceResponse>(StatusCodes.Status201Created)
            .WithTags("InvoiceEndpoints");
    }
}
