using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PayPalService;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public record ReconciliationEntry(
    string? TransactionId,
    string? PaypalReferenceId,
    string? InvoiceId,
    string? Status,
    string? Amount,
    string? Currency,
    string? FeeAmount,
    string? InitiationDate,
    int? EShopOrderId,
    string? EShopOrderStatus);

public class GetReconciliationResponse : BaseResponse
{
    public GetReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public int TotalPayPalTransactions { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new();
}

public class GetReconciliationEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IReadRepository<Order> orderRepo,
                   IPayPalService paypal, CancellationToken ct) =>
            {
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                    return Results.BadRequest("'from' and 'to' query parameters are required.");

                if (!DateTimeOffset.TryParse(from, out _) || !DateTimeOffset.TryParse(to, out _))
                    return Results.BadRequest("'from' and 'to' must be valid ISO-8601 date-times.");

                var transactions = await paypal.GetTransactionsAsync(from, to, ct);

                // Load all payments to match against PayPal transactions
                var allOrders = await orderRepo.ListAsync(new ApplicationCore.Specifications.AllOrdersWithPaymentSpec(), ct);

                // Build lookup: paypal order id / capture id / auth id → eShop order
                var paypalIdToOrder = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
                foreach (var o in allOrders)
                {
                    if (o.Payment == null) continue;
                    if (!string.IsNullOrEmpty(o.Payment.PayPalOrderId))
                        paypalIdToOrder.TryAdd(o.Payment.PayPalOrderId, o);
                    if (!string.IsNullOrEmpty(o.Payment.AuthorizationId))
                        paypalIdToOrder.TryAdd(o.Payment.AuthorizationId, o);
                    if (!string.IsNullOrEmpty(o.Payment.CaptureId))
                        paypalIdToOrder.TryAdd(o.Payment.CaptureId, o);
                }

                var entries = new List<ReconciliationEntry>();
                foreach (var tx in transactions)
                {
                    Order? matched = null;
                    if (!string.IsNullOrEmpty(tx.PaypalReferenceId))
                        paypalIdToOrder.TryGetValue(tx.PaypalReferenceId, out matched);
                    if (matched == null && !string.IsNullOrEmpty(tx.TransactionId))
                        paypalIdToOrder.TryGetValue(tx.TransactionId, out matched);

                    entries.Add(new ReconciliationEntry(
                        TransactionId: tx.TransactionId,
                        PaypalReferenceId: tx.PaypalReferenceId,
                        InvoiceId: tx.InvoiceId,
                        Status: tx.Status,
                        Amount: tx.Amount,
                        Currency: tx.Currency,
                        FeeAmount: tx.FeeAmount,
                        InitiationDate: tx.InitiationDate,
                        EShopOrderId: matched?.Id,
                        EShopOrderStatus: matched?.Status.ToString()));
                }

                return Results.Ok(new GetReconciliationResponse(Guid.NewGuid())
                {
                    From = from,
                    To = to,
                    TotalPayPalTransactions = transactions.Count,
                    Entries = entries
                });
            })
            .Produces<GetReconciliationResponse>()
            .Produces(400)
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.StatusCode(501));
}
