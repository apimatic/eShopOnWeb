using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// In-memory stand-in for the Maxio Advanced Billing API used by the endpoint tests.
/// Mirrors the documented behaviour of the endpoints the app consumes.
/// </summary>
public class FakeMaxioApiClient : IMaxioApiClient
{
    private readonly object _gate = new();
    private int _nextCustomerId = 1000;
    private int _nextSubscriptionId = 9000;

    public List<MaxioCustomer> Customers { get; } = new();
    public List<MaxioSubscription> Subscriptions { get; } = new();

    public int CustomerCreateAttempts { get; private set; }
    public int SubscriptionCreateAttempts { get; private set; }

    public IReadOnlyList<MaxioSubscriptionCreateRequest> SubscriptionCreateRequests { get; }
        = new List<MaxioSubscriptionCreateRequest>();

    public FakeMaxioApiClient()
    {
        Products = new List<MaxioProduct>
        {
            new MaxioProduct
            {
                Id = 1, Name = "Pro Plan", Handle = "eshop-pro", Description = "Pro plan",
                PriceInCents = 29900, Interval = 1, IntervalUnit = "month",
                ProductFamily = new MaxioProductFamily { Handle = "eshop-subscribe", Name = "eShop Subscribe" }
            },
            new MaxioProduct
            {
                Id = 2, Name = "Basic Plan", Handle = "basic-plan", Description = "Basic plan",
                PriceInCents = 2900, Interval = 1, IntervalUnit = "month",
                ProductFamily = new MaxioProductFamily { Handle = "eshop-subscribe", Name = "eShop Subscribe" }
            }
        };
    }

    public IReadOnlyList<MaxioProduct> Products { get; }

    public Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(Customers.FirstOrDefault(c => string.Equals(c.Reference, reference, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreateRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            CustomerCreateAttempts++;
            if (Customers.Any(c => string.Equals(c.Reference, request.Reference, StringComparison.OrdinalIgnoreCase)))
            {
                throw new MaxioApiException(System.Net.HttpStatusCode.UnprocessableEntity,
                    "Reference: must be unique - that value has been taken.");
            }

            var customer = new MaxioCustomer
            {
                Id = _nextCustomerId++,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Reference = request.Reference
            };
            Customers.Add(customer);
            return Task.FromResult(customer);
        }
    }

    public Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<MaxioProduct>>(Products.ToList());
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var result = Subscriptions.Where(s => s.Customer?.Id == customerId).ToList();
            return Task.FromResult<IReadOnlyList<MaxioSubscription>>(result);
        }
    }

    public Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreateRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            SubscriptionCreateAttempts++;
            ((List<MaxioSubscriptionCreateRequest>)SubscriptionCreateRequests).Add(request);

            var customer = Customers.FirstOrDefault(c => c.Id == request.CustomerId);
            if (customer is null)
            {
                throw new MaxioApiException(System.Net.HttpStatusCode.NotFound, "Customer not found");
            }

            var product = Products.FirstOrDefault(p => string.Equals(p.Handle, request.ProductHandle, StringComparison.OrdinalIgnoreCase));
            if (product is null)
            {
                throw new MaxioApiException(System.Net.HttpStatusCode.UnprocessableEntity, "Product not found");
            }

            var now = DateTimeOffset.UtcNow;
            var subscription = new MaxioSubscription
            {
                Id = _nextSubscriptionId++,
                State = "active",
                CreatedAt = now,
                ActivatedAt = now,
                CurrentPeriodEndsAt = now.AddMonths(1),
                NextAssessmentAt = now.AddMonths(1),
                ProductPriceInCents = product.PriceInCents,
                Customer = customer,
                Product = new MaxioProduct
                {
                    Id = product.Id,
                    Name = product.Name,
                    Handle = product.Handle,
                    Description = product.Description,
                    PriceInCents = product.PriceInCents,
                    Interval = product.Interval,
                    IntervalUnit = product.IntervalUnit,
                    ProductFamily = product.ProductFamily
                }
            };
            Subscriptions.Add(subscription);
            return Task.FromResult(subscription);
        }
    }

    public Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<MaxioSite?>(new MaxioSite
        {
            Currency = "USD",
            RelationshipInvoicingEnabled = true,
            DefaultPaymentCollectionMethod = "remittance"
        });
    }
}
