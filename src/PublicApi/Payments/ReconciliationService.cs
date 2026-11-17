using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Finix;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class ReconciliationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IFinixClient _finixClient;

    public ReconciliationService(IRepository<Order> orderRepository, IFinixClient finixClient)
    {
        _orderRepository = orderRepository;
        _finixClient = finixClient;
    }

    public async Task<ReconciliationResponse> GetAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new PaymentValidationException("from must be earlier than or equal to to.");
        }

        var authorizations = await _finixClient.ListAuthorizationsAsync(from, to, cancellationToken);
        var transfers = await _finixClient.ListTransfersAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(
            new OrdersInDateRangeSpecification(from, to),
            cancellationToken);

        var ordersByAuthorization = new Dictionary<string, Order>(StringComparer.Ordinal);
        var ordersByCapture = new Dictionary<string, Order>(StringComparer.Ordinal);
        var ordersByRefund = new Dictionary<string, Order>(StringComparer.Ordinal);
        var ordersByIdempotency = new Dictionary<string, Order>(StringComparer.Ordinal);

        foreach (var order in orders)
        {
            if (!string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
            {
                ordersByAuthorization[order.FinixAuthorizationId!] = order;
            }
            if (!string.IsNullOrWhiteSpace(order.FinixCaptureTransferId))
            {
                ordersByCapture[order.FinixCaptureTransferId!] = order;
            }
            foreach (var refund in order.Refunds)
            {
                if (!string.IsNullOrWhiteSpace(refund.FinixRefundId))
                {
                    ordersByRefund[refund.FinixRefundId] = order;
                }
            }
            if (order.AuthorizationAttempt > 0)
            {
                ordersByIdempotency[IdempotencyFor(order.PaymentReference, order.AuthorizationAttempt)] = order;
            }
        }

        var transactions = new List<ReconciliationFinixTransaction>();
        var orderMatches = orders.ToDictionary(o => o.Id, _ => new List<string>());

        foreach (var authorization in authorizations)
        {
            var transaction = new ReconciliationFinixTransaction
            {
                ResourceType = "AUTHORIZATION",
                Id = authorization.Id,
                CreatedAt = authorization.CreatedAt,
                AmountCents = authorization.Amount,
                Currency = authorization.Currency,
                State = authorization.State,
                Type = "AUTHORIZATION",
                ParentId = authorization.Transfer,
                IdempotencyId = authorization.IdempotencyId
            };

            var order = MatchAuthorization(authorization, ordersByAuthorization, ordersByCapture, ordersByIdempotency);
            if (order is not null)
            {
                transaction.Matched = true;
                transaction.OrderId = order.Id;
                transaction.MatchReason = "authorization id";
                orderMatches[order.Id].Add(authorization.Id);
            }

            transactions.Add(transaction);
        }

        foreach (var transfer in transfers)
        {
            var transaction = new ReconciliationFinixTransaction
            {
                ResourceType = "TRANSFER",
                Id = transfer.Id,
                CreatedAt = transfer.CreatedAt,
                AmountCents = transfer.Amount,
                Currency = transfer.Currency,
                State = transfer.State,
                Type = transfer.Type,
                OperationKey = transfer.OperationKey,
                FeeCents = transfer.Fee,
                ParentId = transfer.ParentTransfer,
                IdempotencyId = transfer.IdempotencyId
            };

            var order = MatchTransfer(transfer, ordersByCapture, ordersByRefund, ordersByIdempotency);
            if (order is not null)
            {
                transaction.Matched = true;
                transaction.OrderId = order.Id;
                transaction.MatchReason = transfer.ParentTransfer is not null
                    ? "refund parent transfer id"
                    : "capture transfer id";
                orderMatches[order.Id].Add(transfer.Id);
            }

            transactions.Add(transaction);
        }

        var reconciliationOrders = orders.Select(order => new ReconciliationOrder
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            TotalCents = order.TotalCents(),
            PaymentStatus = order.PaymentStatus.ToString(),
            FulfilmentStatus = order.FulfilmentStatus.ToString(),
            AuthorizationId = order.FinixAuthorizationId,
            CaptureId = order.FinixCaptureTransferId,
            RefundedAmountCents = order.RefundedAmountCents,
            Matched = orderMatches[order.Id].Count > 0,
            FinixTransactionIds = orderMatches[order.Id]
        }).ToList();

        var unmatchedTransactions = transactions.Where(t => !t.Matched).ToList();
        var unmatchedOrders = reconciliationOrders.Where(o => !o.Matched).ToList();

        return new ReconciliationResponse
        {
            From = from,
            To = to,
            FinixTransactionCount = transactions.Count,
            OrderCount = reconciliationOrders.Count,
            FinixTransactions = transactions,
            Orders = reconciliationOrders,
            UnmatchedFinixTransactions = unmatchedTransactions,
            UnmatchedOrders = unmatchedOrders
        };
    }

    private static Order? MatchAuthorization(
        FinixAuthorization authorization,
        Dictionary<string, Order> ordersByAuthorization,
        Dictionary<string, Order> ordersByCapture,
        Dictionary<string, Order> ordersByIdempotency)
    {
        if (ordersByAuthorization.TryGetValue(authorization.Id, out var order))
        {
            return order;
        }

        if (!string.IsNullOrWhiteSpace(authorization.Transfer) &&
            ordersByCapture.TryGetValue(authorization.Transfer!, out order))
        {
            return order;
        }

        return TryMatchIdempotency(authorization.IdempotencyId, ordersByIdempotency);
    }

    private static Order? MatchTransfer(
        FinixTransfer transfer,
        Dictionary<string, Order> ordersByCapture,
        Dictionary<string, Order> ordersByRefund,
        Dictionary<string, Order> ordersByIdempotency)
    {
        if (ordersByCapture.TryGetValue(transfer.Id, out var order))
        {
            return order;
        }

        if (ordersByRefund.TryGetValue(transfer.Id, out order))
        {
            return order;
        }

        if (!string.IsNullOrWhiteSpace(transfer.ParentTransfer))
        {
            if (ordersByCapture.TryGetValue(transfer.ParentTransfer!, out order))
            {
                return order;
            }
            if (ordersByRefund.TryGetValue(transfer.ParentTransfer!, out order))
            {
                return order;
            }
        }

        return TryMatchIdempotency(transfer.IdempotencyId, ordersByIdempotency);
    }

    private static Order? TryMatchIdempotency(
        string? idempotencyId,
        Dictionary<string, Order> ordersByIdempotency)
    {
        if (string.IsNullOrWhiteSpace(idempotencyId))
        {
            return null;
        }

        return ordersByIdempotency.TryGetValue(idempotencyId!, out var order) ? order : null;
    }

    private static string IdempotencyFor(Guid paymentReference, int attempt) =>
        $"eshop-order-{paymentReference:N}-auth-{attempt}";
}
