using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record ReconciliationReport(
    List<ReconciliationOrderRow> Orders,
    List<UnmatchedTransactionRow> UnmatchedPayPalTransactions
);

public record ReconciliationOrderRow(
    int OrderId,
    string PaymentStatus,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? CaptureId,
    decimal Total
);

public record UnmatchedTransactionRow(
    string TransactionId,
    string? Status,
    string? Amount,
    string? Currency,
    string? Date,
    string? PayPalReferenceId
);

public class ReconciliationEndpoint : IEndpoint<IResult, IRepository<Order>>
{
    private readonly IPayPalPaymentService _payPal;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReconciliationEndpoint(IPayPalPaymentService payPal, IHttpContextAccessor httpContextAccessor)
    {
        _payPal = payPal;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IRepository<Order> orderRepo) =>
            {
                return await HandleAsync(orderRepo);
            })
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IRepository<Order> orderRepo)
    {
        var httpCtx = _httpContextAccessor.HttpContext;
        var ct = httpCtx?.RequestAborted ?? default;
        var query = httpCtx?.Request.Query;
        var from = query != null && query.ContainsKey("from") ? query["from"].ToString() : null;
        var to = query != null && query.ContainsKey("to") ? query["to"].ToString() : null;

        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            return Results.BadRequest("Query parameters 'from' and 'to' are required (ISO-8601 format).");

        var spec = new AllOrdersWithPaymentSpec();
        var orders = await orderRepo.ListAsync(spec, ct);

        var orderRows = orders.Select(o => new ReconciliationOrderRow(
            o.Id, o.PaymentStatus.ToString(), o.PayPalOrderId, o.AuthorizationId, o.CaptureId, o.Total()
        )).ToList();

        var unmatched = new List<UnmatchedTransactionRow>();
        try
        {
            var transactions = await _payPal.SearchTransactionsAsync(from, to, ct);
            var knownCaptureIds = new HashSet<string?>(orders.Select(o => o.CaptureId));
            var knownAuthIds = new HashSet<string?>(orders.Select(o => o.AuthorizationId));

            foreach (var tx in transactions)
            {
                if (!knownCaptureIds.Contains(tx.TransactionId) && !knownAuthIds.Contains(tx.TransactionId))
                {
                    unmatched.Add(new UnmatchedTransactionRow(
                        tx.TransactionId, tx.Status, tx.Amount, tx.Currency, tx.InitiationDate, tx.PayPalReferenceId));
                }
            }
        }
        catch (PayPalPaymentException ex)
        {
            return Results.Problem(detail: $"PayPal transaction search failed: {ex.Message}", statusCode: ex.StatusCode);
        }

        return Results.Ok(new ReconciliationReport(orderRows, unmatched));
    }
}
