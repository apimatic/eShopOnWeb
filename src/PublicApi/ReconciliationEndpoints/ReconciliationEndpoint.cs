using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator report: lists PayPal's own record of transactions for a date range and lines them up
/// against eShop orders, so a payment PayPal knows about that eShop doesn't (or the reverse) is
/// visible. Covers the whole range by paging through PayPal's transaction search.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IRepository<Order>>
{
    private readonly IPaymentProvider _paymentProvider;

    public ReconciliationEndpoint(IPaymentProvider paymentProvider)
    {
        _paymentProvider = paymentProvider;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), orderRepository);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IRepository<Order> orderRepository)
    {
        var transactions = await _paymentProvider.SearchTransactionsAsync(request.From, request.To, CancellationToken.None);

        var orders = await orderRepository.ListAsync(new OrdersByDateRangeSpecification(request.From, request.To));
        var ordersWithPayment = orders.Where(o => o.Payment is not null).ToList();

        var matched = new System.Collections.Generic.List<ReconciliationMatchDto>();
        var payPalOnly = new System.Collections.Generic.List<ReconciliationTransactionDto>();
        var matchedOrderIds = new System.Collections.Generic.HashSet<int>();

        foreach (var txn in transactions)
        {
            var order = ordersWithPayment.FirstOrDefault(o =>
                o.Payment!.CaptureId == txn.TransactionId || o.Payment!.AuthorizationId == txn.TransactionId);

            if (order is not null)
            {
                matched.Add(new ReconciliationMatchDto
                {
                    OrderId = order.Id,
                    TransactionId = txn.TransactionId ?? string.Empty,
                    EShopAmount = order.Total(),
                    PayPalAmount = txn.Amount,
                    PayPalStatus = txn.Status
                });
                matchedOrderIds.Add(order.Id);
            }
            else
            {
                payPalOnly.Add(new ReconciliationTransactionDto
                {
                    TransactionId = txn.TransactionId,
                    Amount = txn.Amount,
                    Currency = txn.Currency,
                    Status = txn.Status,
                    InitiationDate = txn.InitiationDate
                });
            }
        }

        var eShopOnly = ordersWithPayment
            .Where(o => !matchedOrderIds.Contains(o.Id))
            .Select(o => new ReconciliationOrderDto
            {
                OrderId = o.Id,
                CaptureId = o.Payment!.CaptureId,
                AuthorizationId = o.Payment!.AuthorizationId,
                Amount = o.Total(),
                Status = o.Status.ToString()
            })
            .ToList();

        return Results.Ok(new ReconciliationResponse(request.CorrelationId())
        {
            From = request.From,
            To = request.To,
            Matched = matched,
            PayPalOnly = payPalOnly,
            EShopOnly = eShopOnly
        });
    }
}
