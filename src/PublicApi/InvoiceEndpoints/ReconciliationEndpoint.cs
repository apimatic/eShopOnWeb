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

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconciliationResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public ReconciliationSummaryDto Summary { get; set; } = new();
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

public class ReconciliationSummaryDto
{
    public int Total { get; set; }
    public int InSync { get; set; }
    public int AtProviderNotInEShop { get; set; }
    public int InEShopNotAtProvider { get; set; }
    public int External { get; set; }
}

public class ReconciliationEntryDto
{
    public string InvoiceId { get; set; } = string.Empty;
    public bool IsEShopInvoice { get; set; }
    public bool PresentAtProvider { get; set; }
    public bool PresentInEShop { get; set; }
    public bool IsDiscrepancy { get; set; }
    public string? Status { get; set; }
    public int? OrderId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }
    public string? CustomerName { get; set; }
}

/// <summary>
/// Lists the provider's own record of bills raised in a date range and lines them up against what
/// eShop believes it raised, making plain which bills are eShop's and which belong to other
/// activity on the shared provider account. Operator action — restricted to the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IInvoiceService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IInvoiceService invoiceService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), invoiceService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IInvoiceService invoiceService)
    {
        var report = await invoiceService.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            Summary = new ReconciliationSummaryDto
            {
                Total = report.Summary.Total,
                InSync = report.Summary.InSync,
                AtProviderNotInEShop = report.Summary.AtProviderNotInEShop,
                InEShopNotAtProvider = report.Summary.InEShopNotAtProvider,
                External = report.Summary.External
            },
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                InvoiceId = e.InvoiceId,
                IsEShopInvoice = e.IsEShopInvoice,
                PresentAtProvider = e.PresentAtProvider,
                PresentInEShop = e.PresentInEShop,
                IsDiscrepancy = e.IsDiscrepancy,
                Status = e.Status,
                OrderId = e.OrderId,
                Amount = e.Amount,
                Currency = e.Currency,
                DueDate = e.DueDate,
                CreatedDate = e.CreatedDate,
                CustomerName = e.CustomerName
            }).ToList()
        };

        return Results.Ok(response);
    }
}
