using System;
using System.Security.Claims;
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
/// Raises a bill with the provider for an order. What is billed comes from the order — its items and
/// what they cost — not from anything the caller restates. The bill starts out not yet put to the shopper.
/// </summary>
public class CreateInvoiceForOrderEndpoint : IEndpoint
{
    private readonly IInvoiceManagementService _invoiceService;

    public CreateInvoiceForOrderEndpoint(IInvoiceManagementService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RaiseInvoiceRequest request, HttpContext context) =>
            {
                return await HandleAsync(orderId, request, context.User);
            })
            .Produces<InvoiceResponse>(StatusCodes.Status201Created)
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, RaiseInvoiceRequest request, ClaimsPrincipal user)
    {
        var callerId = user.GetCallerId();
        if (string.IsNullOrEmpty(callerId))
        {
            return Results.Unauthorized();
        }

        if (request.DueDate == default)
        {
            return Results.BadRequest("A due date (YYYY-MM-DD) is required to raise a bill.");
        }

        var customer = ToVisaCustomer(request.Customer);
        var snapshot = await _invoiceService.RaiseInvoiceForOrderAsync(orderId, request.DueDate, customer, callerId, user.IsOperator());

        var response = InvoiceResponse.From(snapshot);
        return Results.Created($"api/invoices/{response.InvoiceId}", response);
    }

    private static VisaCustomer? ToVisaCustomer(CustomerDto? dto)
    {
        if (dto is null || (string.IsNullOrWhiteSpace(dto.Name) && string.IsNullOrWhiteSpace(dto.Email)))
        {
            return null;
        }

        return new VisaCustomer(dto.Name ?? string.Empty, dto.Email ?? string.Empty);
    }
}
