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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationReportRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationEntryDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public int? OrderId { get; set; }
}

public class ReconciliationReportResponse : BaseResponse
{
    public ReconciliationReportResponse(Guid correlationId) : base(correlationId) { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> MissingFromPayPal { get; set; } = new();
    public List<ReconciliationEntryDto> MissingFromEShop { get; set; } = new();
}

/// <summary>
/// Operator report: lines up PayPal's own transactions for a date range against eShop's order records.
/// </summary>
public class ReconciliationReportEndpoint : IEndpoint<IResult, ReconciliationReportRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new ReconciliationReportRequest { From = from, To = to }, paymentService);
            })
            .Produces<ReconciliationReportResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationReportRequest request, IOrderPaymentService paymentService)
    {
        var response = new ReconciliationReportResponse(request.CorrelationId());

        var report = await paymentService.GetReconciliationReportAsync(request.From, request.To);

        response.From = report.From;
        response.To = report.To;
        response.Matched = report.Matched.Select(Map).ToList();
        response.MissingFromPayPal = report.MissingFromPayPal.Select(Map).ToList();
        response.MissingFromEShop = report.MissingFromEShop.Select(Map).ToList();
        return Results.Ok(response);
    }

    private static ReconciliationEntryDto Map(Microsoft.eShopWeb.ApplicationCore.PayPal.ReconciliationEntry entry) => new()
    {
        TransactionId = entry.TransactionId,
        Type = entry.Type,
        Amount = entry.Amount,
        CurrencyCode = entry.CurrencyCode,
        Timestamp = entry.Timestamp,
        OrderId = entry.OrderId
    };
}
