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

public class CreateInvoiceForOrderRequest
{
    /// <summary>The calendar date the bill falls due (ISO-8601 date, e.g. 2026-09-30).</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>Optional customer details; defaults to the shopper's identity when omitted.</summary>
    public CustomerDto? Customer { get; set; }
}

public class CreateInvoiceForOrderResponse
{
    public string InvoiceId { get; set; } = string.Empty;
}

/// <summary>
/// Raises a bill with the provider for one of the shopper's orders. What is billed comes from the
/// order itself, not from anything the caller restates; only the due date and (optional) customer
/// details are taken from the request. The bill starts out not yet put to the shopper.
/// </summary>
public class CreateInvoiceForOrderEndpoint : IEndpoint<IResult, int, CreateInvoiceForOrderRequest, HttpContext>
{
    private readonly IInvoiceService _invoiceService;

    public CreateInvoiceForOrderEndpoint(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CreateInvoiceForOrderRequest request, HttpContext http) =>
                await HandleAsync(orderId, request, http))
            .Produces<CreateInvoiceForOrderResponse>(StatusCodes.Status201Created)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, CreateInvoiceForOrderRequest request, HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.DueDate == default)
        {
            return Results.BadRequest(new { error = "A valid due date is required." });
        }

        var customer = ToCustomerDetails(request.Customer);
        var invoiceId = await _invoiceService.RaiseForOrderAsync(orderId, buyerId, request.DueDate, customer);
        if (invoiceId is null)
        {
            // The order does not exist or does not belong to the shopper.
            return Results.NotFound(new { error = $"Order {orderId} was not found." });
        }

        var response = new CreateInvoiceForOrderResponse { InvoiceId = invoiceId };
        return Results.Created($"api/invoices/{invoiceId}", response);
    }

    internal static CustomerDetails? ToCustomerDetails(CustomerDto? dto) =>
        dto is null ? null : new CustomerDetails(dto.Name ?? string.Empty, dto.Email ?? string.Empty);
}
