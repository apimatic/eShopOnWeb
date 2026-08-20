using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public string? From { get; init; }
    public string? To { get; init; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int EshopPaymentCount { get; set; }
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
    public List<ReconciliationRowDto> Items { get; set; } = new();
}

public class ReconciliationRowDto
{
    public string MatchKind { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? PayPalReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public decimal? FeeAmount { get; set; }
    public string? Currency { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IPaymentReconciliationService reconciliation) =>
                await HandleAsync(new ReconciliationRequest { From = from, To = to }, reconciliation))
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentReconciliationService reconciliation)
    {
        var from = ParseRequired(request.From, "from");
        var to = ParseRequired(request.To, "to");
        var report = await reconciliation.ReconcileAsync(from, to);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            EshopPaymentCount = report.EshopPaymentCount,
            MatchedCount = report.MatchedCount,
            PayPalOnlyCount = report.PayPalOnlyCount,
            EshopOnlyCount = report.EshopOnlyCount,
            Items = report.Matches.Select(m => new ReconciliationRowDto
            {
                MatchKind = m.MatchKind,
                OrderId = m.OrderId,
                PayPalTransactionId = m.PayPalTransaction?.TransactionId,
                PayPalReferenceId = m.PayPalTransaction?.ReferenceId,
                EventCode = m.PayPalTransaction?.EventCode,
                Status = m.PayPalTransaction?.Status,
                Amount = m.PayPalTransaction?.Amount,
                FeeAmount = m.PayPalTransaction?.FeeAmount,
                Currency = m.PayPalTransaction?.Currency,
                InvoiceId = m.PayPalTransaction?.InvoiceId,
                CustomField = m.PayPalTransaction?.CustomField,
                InitiationDate = m.PayPalTransaction?.InitiationDate
            }).ToList()
        };

        return Results.Ok(response);
    }

    private static DateTimeOffset ParseRequired(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new PaymentException($"`{name}` must be an ISO-8601 date-time.");
        }

        return parsed;
    }
}
