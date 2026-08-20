using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationTransactionDto
{
    public string MatchStatus { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string? PaypalReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? InitiationTime { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public int? OrderId { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<ReconciliationTransactionDto> PayPalTransactions { get; set; } = new();
    public System.Collections.Generic.List<OrderDto> EshopOnlyOrders { get; set; } = new();
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    private readonly ICheckoutService _checkout;

    public ReconciliationEndpoint(ICheckoutService checkout)
    {
        _checkout = checkout;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactions = report.PayPalTransactions.Select(row => new ReconciliationTransactionDto
            {
                MatchStatus = row.MatchStatus,
                TransactionId = row.PayPal.TransactionId,
                PaypalReferenceId = row.PayPal.PaypalReferenceId,
                InvoiceId = row.PayPal.InvoiceId,
                CustomField = row.PayPal.CustomField,
                EventCode = row.PayPal.EventCode,
                Status = row.PayPal.Status,
                InitiationTime = row.PayPal.InitiationTime,
                Amount = row.PayPal.Amount,
                Currency = row.PayPal.Currency,
                Fee = row.PayPal.Fee,
                OrderId = row.Order?.Id
            }).ToList(),
            EshopOnlyOrders = report.EshopOnlyOrders.Select(o => OrderDtoMapper.ToDto(o, _checkout.Currency)).ToList()
        };
        return Results.Ok(response);
    }
}
