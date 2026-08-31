using System;
using System.Collections.Generic;
using System.Linq;
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

/// <summary>The caller's own bills, each showing where it has got to.</summary>
public class MyInvoicesEndpoint : IEndpoint<IResult, ClaimsPrincipal, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-invoices",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IInvoiceService invoiceService) =>
                await HandleAsync(user, invoiceService))
            .Produces<MyInvoicesResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IInvoiceService invoiceService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var invoices = await invoiceService.GetInvoicesForBuyerAsync(buyerId);

        var response = new MyInvoicesResponse
        {
            Invoices = invoices.Select(invoice => new MyInvoiceDto
            {
                InvoiceId = invoice.ProviderInvoiceId,
                InvoiceNumber = invoice.InvoiceNumber,
                OrderId = invoice.OrderId,
                Status = invoice.Status.ToString(),
                Amount = invoice.Amount,
                Currency = invoice.Currency,
                DueDate = invoice.DueDate,
                CreatedDate = invoice.CreatedDate
            }).ToList()
        };

        return Results.Ok(response);
    }
}

public class MyInvoicesResponse : BaseResponse
{
    public List<MyInvoiceDto> Invoices { get; set; } = new();
}

public class MyInvoiceDto
{
    /// <summary>The provider identifier, which the operator endpoints act on.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    public string InvoiceNumber { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
}
