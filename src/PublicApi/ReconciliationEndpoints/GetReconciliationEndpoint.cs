using System;
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

/// <summary>
/// Operator report: PayPal's own transaction record for [from, to] lined up against
/// eShop orders, so a payment known to one side and not the other is visible. Covers the
/// whole range (all pages, all provider-supported windows).
/// </summary>
public class GetReconciliationEndpoint : IEndpoint
{
    private readonly IReconciliationService _reconciliation;

    public GetReconciliationEndpoint(IReconciliationService reconciliation)
    {
        _reconciliation = reconciliation;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTime? from, DateTime? to) =>
            {
                return await HandleAsync(from, to);
            })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTime? from, DateTime? to)
    {
        if (from is null || to is null)
        {
            throw new ValidationFailureException("Both 'from' and 'to' (ISO-8601 date-times) are required.");
        }

        var report = await _reconciliation.BuildAsync(ToUtc(from.Value), ToUtc(to.Value));

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            GeneratedAt = report.GeneratedAt,
            MatchedCount = report.MatchedCount,
            PayPalOnlyCount = report.PayPalOnlyCount,
            EshopOnlyCount = report.EshopOnlyCount,
            CoverageNote = report.CoverageNote,
            Rows = report.Rows.Select(row => new ReconciliationRowDto
            {
                MatchState = row.MatchState.ToString(),
                TransactionId = row.TransactionId,
                TransactionStatus = row.TransactionStatus,
                TransactionEventCode = row.TransactionEventCode,
                Amount = row.ProviderAmount,
                FeeAmount = row.ProviderFeeAmount,
                NetAmount = row.ProviderNetAmount,
                Currency = row.ProviderCurrency,
                ProviderInvoiceId = row.ProviderInvoiceId,
                ProviderReferenceId = row.ProviderReferenceId,
                OrderId = row.OrderId,
                OrderStatus = row.OrderStatus,
                OrderTotal = row.OrderAmount,
                OrderBuyerId = row.OrderBuyerId,
                OrderPaymentSummary = row.OrderPaymentSummary,
                TransactionDate = row.TransactionDate
            }).ToList()
        };

        return Results.Ok(response);
    }

    private static DateTimeOffset ToUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(value, TimeSpan.Zero),
            DateTimeKind.Local => new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero),
            // No zone in the query string: treat as UTC.
            _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero)
        };
}
