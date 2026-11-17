using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Process-local payment store. The PublicApi in-memory configuration is single-run and
/// host-isolated, so this is the durable-within-a-run record of orders, saved cards,
/// refunds and duplicate claims.
/// </summary>
public sealed class PaymentStore
{
    private readonly object _sync = new();
    private readonly Dictionary<int, OrderPaymentRecord> _orders = new();
    private readonly Dictionary<string, SavedCardRecord> _cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClaimEntry> _claims = new(StringComparer.Ordinal);
    private int _nextOrderId;

    public int NextOrderId() => Interlocked.Increment(ref _nextOrderId);

    public void AddOrder(OrderPaymentRecord order)
    {
        lock (_sync)
        {
            _orders[order.OrderId] = order;
        }
    }

    public OrderPaymentRecord? GetOrder(int orderId)
    {
        lock (_sync)
        {
            return _orders.TryGetValue(orderId, out var order) ? order : null;
        }
    }

    public OrderPaymentRecord? GetOrderForBuyer(int orderId, string buyerId)
    {
        lock (_sync)
        {
            return _orders.TryGetValue(orderId, out var order)
                && string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal)
                ? order
                : null;
        }
    }

    public IReadOnlyList<OrderPaymentRecord> GetOrdersForBuyer(string buyerId)
    {
        lock (_sync)
        {
            return _orders.Values
                .Where(o => string.Equals(o.BuyerId, buyerId, StringComparison.Ordinal))
                .OrderByDescending(o => o.CreatedAt)
                .ToList();
        }
    }

    public IReadOnlyList<OrderPaymentRecord> GetAllOrders()
    {
        lock (_sync)
        {
            return _orders.Values.OrderBy(o => o.OrderId).ToList();
        }
    }

    public T WithOrder<T>(int orderId, Func<OrderPaymentRecord, T> action)
    {
        lock (_sync)
        {
            if (!_orders.TryGetValue(orderId, out var order))
            {
                throw new InvalidOperationException($"Order {orderId} does not exist.");
            }

            return action(order);
        }
    }

    public void AddCard(SavedCardRecord card)
    {
        lock (_sync)
        {
            _cards[card.PaymentMethodId] = card;
        }
    }

    public SavedCardRecord? GetCardForBuyer(string paymentMethodId, string buyerId)
    {
        lock (_sync)
        {
            return _cards.TryGetValue(paymentMethodId, out var card)
                && string.Equals(card.BuyerId, buyerId, StringComparison.Ordinal)
                ? card
                : null;
        }
    }

    public SavedCardRecord? FindCardForBuyer(string buyerId, string last4, int expirationMonth, int expirationYear, string? name)
    {
        lock (_sync)
        {
            return _cards.Values.FirstOrDefault(card =>
                string.Equals(card.BuyerId, buyerId, StringComparison.Ordinal)
                && card.Enabled
                && string.Equals(card.Last4, last4, StringComparison.Ordinal)
                && card.ExpirationMonth == expirationMonth
                && card.ExpirationYear == expirationYear
                && string.Equals(card.Name ?? string.Empty, name ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }
    }

    public SavedCardRecord? FindCardByFinixId(string finixPaymentInstrumentId)
    {
        lock (_sync)
        {
            return _cards.Values.FirstOrDefault(card =>
                string.Equals(card.FinixPaymentInstrumentId, finixPaymentInstrumentId, StringComparison.Ordinal));
        }
    }

    public IReadOnlyList<SavedCardRecord> GetCardsForBuyer(string buyerId)
    {
        lock (_sync)
        {
            return _cards.Values
                .Where(card => string.Equals(card.BuyerId, buyerId, StringComparison.Ordinal) && card.Enabled)
                .OrderByDescending(card => card.CreatedAt)
                .ToList();
        }
    }

    public T WithCard<T>(string paymentMethodId, Func<SavedCardRecord, T> action)
    {
        lock (_sync)
        {
            if (!_cards.TryGetValue(paymentMethodId, out var card))
            {
                throw new InvalidOperationException($"Payment method {paymentMethodId} does not exist.");
            }

            return action(card);
        }
    }

    /// <summary>
    /// Starts an action claim. Returns <c>null</c> when the caller owns the claim, otherwise
    /// the existing entry (in progress or completed).
    /// </summary>
    public ClaimEntry? BeginClaim(string key)
    {
        lock (_sync)
        {
            if (_claims.TryGetValue(key, out var existing))
            {
                return existing;
            }

            _claims[key] = new ClaimEntry { Key = key };
            return null;
        }
    }

    public ClaimEntry? GetClaim(string key)
    {
        lock (_sync)
        {
            return _claims.TryGetValue(key, out var existing) ? existing : null;
        }
    }

    public void CompleteClaim(string key, int statusCode, object? body)
    {
        lock (_sync)
        {
            if (_claims.TryGetValue(key, out var claim))
            {
                claim.Completed = true;
                claim.StatusCode = statusCode;
                claim.Body = body;
                claim.CompletedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    public void ReleaseClaim(string key)
    {
        lock (_sync)
        {
            _claims.Remove(key);
        }
    }
}
