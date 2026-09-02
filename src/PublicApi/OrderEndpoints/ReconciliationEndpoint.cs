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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range (ISO-8601, covering
/// every page of the range) lined up against eShop orders, so a payment PayPal knows about and
/// eShop doesn't — or the reverse — is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentService paymentService) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, paymentService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService paymentService)
    {
        var report = await paymentService.GetReconciliationAsync(request.From, request.To);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            Transactions = report.Transactions.Select(Map).ToList(),
            OrdersMissingFromPayPal = report.OrdersMissingFromPayPal.ToList()
        };
        return Results.Ok(response);
    }

    private static ReconciliationEntryDto Map(ReconciliationEntry entry) => new()
    {
        TransactionId = entry.Transaction.TransactionId,
        ReferenceId = entry.Transaction.ReferenceId,
        ReferenceIdType = entry.Transaction.ReferenceIdType,
        EventCode = entry.Transaction.EventCode,
        Status = entry.Transaction.Status,
        Amount = entry.Transaction.Amount,
        Fee = entry.Transaction.Fee,
        Currency = entry.Transaction.Currency,
        InvoiceId = entry.Transaction.InvoiceId,
        CustomField = entry.Transaction.CustomField,
        UpdatedAt = entry.Transaction.UpdatedAt,
        MatchedOrderId = entry.MatchedOrderId
    };
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public List<ReconciliationEntryDto> Transactions { get; set; } = new();
    public List<int> OrdersMissingFromPayPal { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string? TransactionId { get; set; }
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Fee { get; set; }
    public string? Currency { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int? MatchedOrderId { get; set; }
}
