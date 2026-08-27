using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator: lists PayPal's own record of transactions for a date range (fully
/// paged) lined up against eShop orders, so a payment known on only one side is
/// visible. from/to are ISO-8601 date-times.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, CancellationToken>
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IReadRepository<Order> _orderRepository;

    public GetReconciliationEndpoint(IPaymentGateway paymentGateway, IReadRepository<Order> orderRepository)
    {
        _paymentGateway = paymentGateway;
        _orderRepository = orderRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            {
                return await HandleAsync(new GetReconciliationRequest(from, to), ct);
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, CancellationToken ct)
    {
        if (request.To <= request.From)
        {
            throw new PaymentDomainException("'to' must be after 'from'.", 400);
        }

        var transactions = await _paymentGateway.SearchTransactionsAsync(request.From, request.To, ct);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentsSpecification(), ct);

        var response = new GetReconciliationResponse
        {
            From = request.From,
            To = request.To
        };

        var matchedTransactionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in transactions)
        {
            var order = FindMatchingOrder(tx, orders);
            if (order != null && tx.TransactionId != null)
            {
                matchedTransactionIds.Add(tx.TransactionId);
            }

            response.Transactions.Add(new ReconciliationTransactionDto
            {
                TransactionId = tx.TransactionId,
                ReferenceId = tx.ReferenceId,
                ReferenceIdType = tx.ReferenceIdType,
                InvoiceId = tx.InvoiceId,
                CustomField = tx.CustomField,
                Amount = tx.Amount,
                Currency = tx.Currency,
                Fee = tx.Fee,
                Status = tx.Status,
                EventCode = tx.EventCode,
                Time = tx.Time,
                MatchedOrderId = order?.Id
            });
        }

        // Local payments PayPal did not report inside the range.
        var paypalIds = transactions
            .SelectMany(t => new[] { t.TransactionId, t.ReferenceId })
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var order in orders.Where(o => o.Payment != null))
        {
            var payment = order.Payment!;
            var knownIds = new List<string?> { payment.PayPalOrderId, payment.AuthorizationId, payment.CaptureId };
            knownIds.AddRange(payment.Refunds.Select(r => r.PayPalRefundId));

            var represented = knownIds.Any(id => id != null && paypalIds.Contains(id));
            if (!represented)
            {
                response.OrdersMissingFromPayPal.Add(new UnmatchedOrderDto
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                    PayPalOrderId = payment.PayPalOrderId,
                    AuthorizationId = payment.AuthorizationId,
                    CaptureId = payment.CaptureId
                });
            }
        }

        response.UnmatchedPayPalTransactionCount = response.Transactions.Count(t => t.MatchedOrderId == null);
        return Results.Ok(response);
    }

    private static Order? FindMatchingOrder(GatewayTransaction tx, IReadOnlyList<Order> orders)
    {
        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment == null)
            {
                continue;
            }

            if (payment.InvoiceId != null && (tx.InvoiceId == payment.InvoiceId || tx.CustomField == payment.InvoiceId))
            {
                return order;
            }

            var localIds = new List<string?> { payment.PayPalOrderId, payment.AuthorizationId, payment.CaptureId };
            localIds.AddRange(payment.Refunds.Select(r => r.PayPalRefundId));
            if (localIds.Any(id => id != null && (id == tx.TransactionId || id == tx.ReferenceId)))
            {
                return order;
            }
        }
        return null;
    }
}
