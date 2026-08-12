using System;
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
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, IRepository<Order>, IRepository<PaymentReference>, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
             AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IRepository<Order> orderRepo, IRepository<PaymentReference> paymentRepo, IPaymentService paymentService) =>
            {
                return await HandleAsync(orderRepo, paymentRepo, paymentService, from, to);
            })
            .Produces<ReconciliationResponse>()
            .WithName("Reconciliation")
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IRepository<Order> orderRepo, IRepository<PaymentReference> paymentRepo,
        IPaymentService paymentService, string from, string to)
    {
        if (!DateTime.TryParse(from, out var fromDate) || !DateTime.TryParse(to, out var toDate))
            return Results.BadRequest("Invalid date format. Use ISO-8601 format (e.g., 2024-01-01T00:00:00Z)");

        try
        {
            var transactions = await paymentService.GetTransactionsAsync(fromDate, toDate);
            var paymentRefs = (await paymentRepo.ListAllAsync()).ToList();

            var reconciliation = new ReconciliationResponse
            {
                FromDate = fromDate,
                ToDate = toDate,
                PayPalTransactions = transactions.Select(t => new PayPalTransactionDto
                {
                    TransactionId = t.TransactionId,
                    Status = t.Status,
                    Amount = t.Amount,
                    Currency = t.Currency,
                    CreatedAt = t.CreatedAt,
                    InvoiceId = t.InvoiceId
                }).ToList(),
                EShopOrders = paymentRefs
                    .Where(p => p.UpdatedAt >= fromDate && p.UpdatedAt <= toDate)
                    .Select(p => new EShopOrderDto
                    {
                        OrderId = p.OrderId,
                        PayPalOrderId = p.PayPalOrderId,
                        State = p.State.ToString(),
                        AuthorizationId = p.AuthorizationId,
                        CaptureId = p.CaptureId,
                        Amount = p.AuthorizedAmount ?? 0m,
                        RefundedAmount = p.RefundedAmount,
                        CreatedAt = p.CreatedAt,
                        UpdatedAt = p.UpdatedAt
                    }).ToList()
            };

            return Results.Ok(reconciliation);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public record ReconciliationResponse
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public List<PayPalTransactionDto> PayPalTransactions { get; set; } = new();
    public List<EShopOrderDto> EShopOrders { get; set; } = new();
}

public record PayPalTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? InvoiceId { get; set; }
}

public record EShopOrderDto
{
    public int OrderId { get; set; }
    public string? PayPalOrderId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal Amount { get; set; }
    public decimal RefundedAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
