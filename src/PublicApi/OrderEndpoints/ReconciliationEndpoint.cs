using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Operator: reconciles PayPal transactions against eShop orders for a date range.</summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from,
                string to,
                IRepository<Order> orderRepo,
                IPayPalPaymentService payPalService,
                CancellationToken ct) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromDate))
                    return Results.BadRequest(new { error = "Invalid 'from' date. Use ISO-8601 format." });
                if (!DateTimeOffset.TryParse(to, out var toDate))
                    return Results.BadRequest(new { error = "Invalid 'to' date. Use ISO-8601 format." });

                IReadOnlyList<TransactionRecord> transactions;
                try
                {
                    transactions = await payPalService.SearchTransactionsAsync(fromDate, toDate, ct);
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(ex.Message, statusCode: 502);
                }

                // Load all orders that have a PayPal payment to enable reconciliation
                var orders = await orderRepo.ListAsync(new AllOrdersWithPaymentSpec(), ct);
                var ordersByPayPalId = orders
                    .Where(o => o.Payment?.PayPalOrderId != null)
                    .ToDictionary(o => o.Payment!.PayPalOrderId!);

                var rows = transactions.Select(tx =>
                {
                    // Match by CustomField (eShop order ID stored at create time)
                    Order? matchedOrder = null;
                    if (tx.CustomField != null && int.TryParse(tx.CustomField, out var eShopOrderId))
                        matchedOrder = orders.FirstOrDefault(o => o.Id == eShopOrderId);

                    return new ReconciliationRow(
                        tx.TransactionId,
                        tx.Status,
                        tx.Amount,
                        tx.Currency,
                        tx.Fee,
                        tx.InitiationDate,
                        tx.CustomField,
                        matchedOrder?.Id,
                        matchedOrder?.Payment?.PaymentStatus,
                        matchedOrder == null ? "PayPalOnly" : "Matched");
                }).ToList();

                // Unmatched eShop orders (have PayPal data but no PayPal transaction in the range)
                var matchedOrderIds = rows.Where(r => r.EShopOrderId.HasValue).Select(r => r.EShopOrderId!.Value).ToHashSet();
                var unmatchedOrders = orders
                    .Where(o => o.Payment?.AuthorizationId != null && !matchedOrderIds.Contains(o.Id))
                    .Select(o => new ReconciliationRow(
                        null, null, null, null, null, null,
                        o.Id.ToString(),
                        o.Id,
                        o.Payment!.PaymentStatus,
                        "EShopOnly"))
                    .ToList();

                return Results.Ok(new ReconciliationResponse(fromDate, toDate, rows.Concat(unmatchedOrders).ToList()));
            })
            .Produces<ReconciliationResponse>()
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IRepository<Order> repo)
        => throw new System.NotImplementedException();
}

public record ReconciliationRequest(string From, string To);
public record ReconciliationRow(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InitiationDate,
    string? CustomField,
    int? EShopOrderId,
    string? EShopPaymentStatus,
    string MatchStatus);
public record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To, List<ReconciliationRow> Rows);
