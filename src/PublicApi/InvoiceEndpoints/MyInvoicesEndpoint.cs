using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class MyInvoicesRequest : BaseRequest
{
    public MyInvoicesRequest(string buyerId) => BuyerId = buyerId;
    public string BuyerId { get; }
}

public class MyInvoicesResponse : BaseResponse
{
    public MyInvoicesResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MyInvoicesResponse()
    {
    }

    public List<MyInvoiceItem> Invoices { get; set; } = new();
}

public class MyInvoiceItem
{
    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public bool IsIssued { get; set; }
    public bool IsWithdrawn { get; set; }
}

/// <summary>
/// Lists the caller's own bills, each showing where it has got to. Shopper-scoped.
/// </summary>
public class MyInvoicesEndpoint : IEndpoint<IResult, MyInvoicesRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-invoices",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IInvoiceService invoiceService) =>
            {
                var buyerId = http.User.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(new MyInvoicesRequest(buyerId), invoiceService);
            })
            .Produces<MyInvoicesResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(MyInvoicesRequest request, IInvoiceService invoiceService)
    {
        var invoices = await invoiceService.GetInvoicesForBuyerAsync(request.BuyerId);

        var response = new MyInvoicesResponse(request.CorrelationId())
        {
            Invoices = invoices.Select(i => new MyInvoiceItem
            {
                InvoiceId = i.InvoiceId,
                OrderId = i.OrderId,
                Status = i.Status,
                Amount = i.Amount,
                Currency = i.Currency,
                DueDate = i.DueDate,
                IsIssued = i.IsIssued,
                IsWithdrawn = i.IsWithdrawn
            }).ToList()
        };

        return Results.Ok(response);
    }
}
