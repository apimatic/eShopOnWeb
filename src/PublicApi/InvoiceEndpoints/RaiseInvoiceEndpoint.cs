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
/// Raises a bill with the provider for one of the shopper's own orders. What is billed comes from the
/// order — its items and what they cost — not from anything the caller restates. The request carries
/// only the due date and the customer details the bill should show. The bill starts out in DRAFT — not
/// yet put to the shopper.
/// </summary>
public class RaiseInvoiceEndpoint : IEndpoint<IResult, RaiseInvoiceRequest, IInvoiceService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RaiseInvoiceRequest request, IInvoiceService invoiceService, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, invoiceService, user);
            })
            .Produces<RaiseInvoiceResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(
        RaiseInvoiceRequest request,
        IInvoiceService invoiceService,
        ClaimsPrincipal user)
    {
        if (request.DueDate == default)
        {
            return Results.BadRequest("A due date is required.");
        }
        if (string.IsNullOrWhiteSpace(request.CustomerName) || string.IsNullOrWhiteSpace(request.CustomerEmail))
        {
            return Results.BadRequest("Customer name and email are required.");
        }

        var raised = await invoiceService.RaiseInvoiceAsync(
            user.GetBuyerId(), user.IsAdministrator(), request.OrderId,
            request.DueDate, request.CustomerName, request.CustomerEmail);

        var response = new RaiseInvoiceResponse(request.CorrelationId())
        {
            InvoiceId = raised.InvoiceId,
            OrderId = request.OrderId,
            Status = raised.Status
        };
        return Results.Created($"api/invoices/{raised.InvoiceId}", response);
    }
}

public class RaiseInvoiceRequest : BaseRequest
{
    /// <summary>Set from the route; the amount and items always come from this order.</summary>
    public int OrderId { get; set; }

    /// <summary>The calendar date the bill falls due.</summary>
    public DateTime DueDate { get; set; }

    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
}
