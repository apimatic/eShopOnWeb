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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record ReconciliationWindow(DateTimeOffset From, DateTimeOffset To);

public class ReconciliationEntryDto
{
    public string? EShopOrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? InvoiceId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? Date { get; set; }
    public string Note { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int EShopOrderCount { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> MissingInEShop { get; set; } = new();
    public List<ReconciliationEntryDto> MissingInPayPal { get; set; } = new();
}

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range and lines them up against
/// eShop orders, covering the whole range (not just its first page).
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationWindow, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationWindow(from, to), service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationWindow window, IReconciliationService service)
    {
        var report = await service.BuildReportAsync(window.From, window.To);

        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            EShopOrderCount = report.EShopOrderCount,
            Matched = report.Matched.Select(ToDto).ToList(),
            MissingInEShop = report.MissingInEShop.Select(ToDto).ToList(),
            MissingInPayPal = report.MissingInPayPal.Select(ToDto).ToList()
        });
    }

    private static ReconciliationEntryDto ToDto(ReconciliationEntry e) => new()
    {
        EShopOrderId = e.EShopOrderId,
        PayPalTransactionId = e.PayPalTransactionId,
        InvoiceId = e.InvoiceId,
        Amount = e.Amount,
        Currency = e.Currency,
        Status = e.Status,
        Date = e.Date,
        Note = e.Note
    };
}
