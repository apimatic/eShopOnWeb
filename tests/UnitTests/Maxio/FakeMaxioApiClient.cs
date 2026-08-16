using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// In-memory fake of the Maxio API used to unit test <c>MaxioBillingService</c> orchestration
/// (idempotency, mapping) without hitting the network.
/// </summary>
internal sealed class FakeMaxioApiClient : IMaxioApiClient
{
    private readonly object _sync = new();
    private readonly Dictionary<string, MaxioCustomer> _customersByReference = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MaxioSubscription> _subscriptions = new();
    private int _nextId = 1000;

    public List<MaxioProduct> Products { get; } = new();

    public int CreateCustomerCalls;
    public int CreateSubscriptionCalls;

    /// <summary>Optional artificial latency for create operations to widen concurrency windows.</summary>
    public TimeSpan CreateDelay { get; set; } = TimeSpan.Zero;

    public Task<IReadOnlyList<MaxioProduct>> ListProductsForProductFamilyAsync(
        string productFamilyIdentifier, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MaxioProduct>>(Products.ToList());

    public Task<MaxioCustomer?> LookupCustomerByReferenceAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _customersByReference.TryGetValue(reference, out var customer);
            return Task.FromResult(customer);
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref CreateCustomerCalls);
        if (CreateDelay > TimeSpan.Zero)
        {
            await Task.Delay(CreateDelay, cancellationToken);
        }

        lock (_sync)
        {
            var reference = request.Customer.Reference ?? string.Empty;
            if (_customersByReference.TryGetValue(reference, out var existing))
            {
                // Simulate Maxio's unique-reference constraint (HTTP 422).
                throw new MaxioApiException(
                    System.Net.HttpStatusCode.UnprocessableEntity,
                    new[] { "reference: has already been taken" },
                    null);
            }

            var customer = new MaxioCustomer
            {
                Id = _nextId++,
                FirstName = request.Customer.FirstName,
                LastName = request.Customer.LastName,
                Email = request.Customer.Email,
                Reference = reference,
            };
            _customersByReference[reference] = customer;
            return customer;
        }
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var subs = _subscriptions.Where(s => s.Customer?.Id == customerId).ToList();
            return Task.FromResult<IReadOnlyList<MaxioSubscription>>(subs);
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref CreateSubscriptionCalls);
        if (CreateDelay > TimeSpan.Zero)
        {
            await Task.Delay(CreateDelay, cancellationToken);
        }

        lock (_sync)
        {
            var handle = request.Subscription.ProductHandle;
            var product = Products.FirstOrDefault(p =>
                string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));
            if (product is null)
            {
                throw new MaxioApiException(
                    System.Net.HttpStatusCode.UnprocessableEntity,
                    new[] { $"Product with API Handle '{handle}' does not exist for this site." },
                    null);
            }

            var customer = _customersByReference.Values.FirstOrDefault(c => c.Id == request.Subscription.CustomerId);
            var subscription = new MaxioSubscription
            {
                Id = _nextId++,
                State = "active",
                ProductPriceInCents = product.PriceInCents,
                Currency = "USD",
                CurrentPeriodStartedAt = DateTimeOffset.UnixEpoch,
                CurrentPeriodEndsAt = DateTimeOffset.UnixEpoch.AddMonths(1),
                NextAssessmentAt = DateTimeOffset.UnixEpoch.AddMonths(1),
                CreatedAt = DateTimeOffset.UnixEpoch,
                Customer = customer,
                Product = product,
            };
            _subscriptions.Add(subscription);
            return subscription;
        }
    }

    /// <summary>Seeds an existing subscription for a customer (test setup helper).</summary>
    public void SeedSubscription(MaxioCustomer customer, string productHandle, string state)
    {
        lock (_sync)
        {
            _customersByReference[customer.Reference ?? string.Empty] = customer;
            _subscriptions.Add(new MaxioSubscription
            {
                Id = _nextId++,
                State = state,
                Customer = customer,
                Product = new MaxioProduct { Handle = productHandle, Name = productHandle },
            });
        }
    }
}
