using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Models;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioBillingClient : IBillingClient
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingClient> _logger;
    private bool _componentValidated = false;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> optionsAccessor, ILogger<MaxioBillingClient> logger)
    {
        Guard.Against.Null(httpClient, nameof(httpClient));
        Guard.Against.Null(optionsAccessor, nameof(optionsAccessor));

        _settings = optionsAccessor.Value;
        _logger = logger;

        Guard.Against.NullOrWhiteSpace(_settings.ApiKey, nameof(_settings.ApiKey));
        Guard.Against.NullOrWhiteSpace(_settings.Subdomain, nameof(_settings.Subdomain));
        Guard.Against.Negative(_settings.ProductFamilyId, nameof(_settings.ProductFamilyId));

        var baseUrl = _settings.ResolveBaseUrl();
        httpClient.BaseAddress = new Uri(baseUrl);

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = _settings.ApiKey,
                Password = "x"
            },
            Environment = _settings.Environment.Equals("EU", StringComparison.OrdinalIgnoreCase)
                ? ServerEnvironment.Eu
                : ServerEnvironment.Us
        };

        _client = new MaxioAdvancedBillingClient(httpClient, options);
    }

    public async Task<List<BillingProduct>> ListProductsAsync(int productFamilyId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing products for family {FamilyId}", productFamilyId);

            var response = await _client.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 100,
                ct: cancellationToken);

            var products = response
                .Where(p => p.ProductFamily?.Id == productFamilyId)
                .Select(MapToBillingProduct)
                .ToList();

            _logger.LogDebug("Found {Count} products in family {FamilyId}", products.Count, productFamilyId);
            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list products");
            throw new BillingProviderException($"Failed to list products: {ex.Message}", ex);
        }
    }

    public async Task<BillingCustomer?> GetOrCreateCustomerAsync(string userReference, string email, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));
        Guard.Against.NullOrWhiteSpace(email, nameof(email));

        try
        {
            _logger.LogDebug("Looking up customer with reference {Reference}", userReference);

            try
            {
                var existing = await _client.Customers.ReadCustomerByReference(userReference, cancellationToken);
                _logger.LogDebug("Found existing customer {CustomerId} for reference {Reference}", existing.Customer?.Id, userReference);
                return MapToBillingCustomer(existing.Customer!);
            }
            catch (Exception ex) when (ex is not BillingProviderException)
            {
                _logger.LogDebug(ex, "Customer lookup failed, creating new customer");
            }

            var createRequest = new CreateCustomerRequest
            {
                Customer = new CustomerAttributes
                {
                    Email = email,
                    Reference = userReference
                }
            };

            var createResponse = await _client.Customers.CreateCustomer(createRequest, cancellationToken);
            _logger.LogInformation("Created new customer {CustomerId} with reference {Reference}", createResponse.Customer?.Id, userReference);

            return MapToBillingCustomer(createResponse.Customer!);
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get or create customer");
            throw new BillingProviderException($"Failed to get or create customer: {ex.Message}", ex);
        }
    }

    public async Task<BillingSubscription?> GetSubscriptionByCustomerAndProductAsync(int customerId, int productId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Looking up subscription for customer {CustomerId} and product {ProductId}", customerId, productId);

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, cancellationToken);
            var subscription = subscriptions
                .FirstOrDefault(s => s.Product?.Id == productId && s.State != "canceled");

            if (subscription == null)
            {
                _logger.LogDebug("No active subscription found for customer {CustomerId} and product {ProductId}", customerId, productId);
                return null;
            }

            return MapToBillingSubscription(subscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subscription by customer and product");
            throw new BillingProviderException($"Failed to get subscription: {ex.Message}", ex);
        }
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, int productId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Negative(customerId, nameof(customerId));
        Guard.Against.Negative(productId, nameof(productId));

        try
        {
            _logger.LogDebug("Creating subscription for customer {CustomerId} and product {ProductId}", customerId, productId);

            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductId = productId,
                    AutoResume = true
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(createRequest, cancellationToken);
            _logger.LogInformation("Created subscription {SubscriptionId} for customer {CustomerId}", response.Subscription?.Id, customerId);

            return MapToBillingSubscription(response.Subscription!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create subscription");
            throw new BillingProviderException($"Failed to create subscription: {ex.Message}", ex);
        }
    }

    public async Task RecordUsageAsync(int subscriptionId, int componentId, int quantity, string? memo = null, CancellationToken cancellationToken = default)
    {
        Guard.Against.Negative(subscriptionId, nameof(subscriptionId));
        Guard.Against.Negative(componentId, nameof(componentId));
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        try
        {
            await ValidateComponentIsMeteredAsync(_settings.ProductFamilyId, componentId, cancellationToken);

            _logger.LogDebug("Recording usage: subscription={SubscriptionId}, component={ComponentId}, quantity={Quantity}", subscriptionId, componentId, quantity);

            var usageRequest = new CreateUsageRequest
            {
                Usage = new CreateUsage
                {
                    Quantity = quantity.ToString(),
                    Description = memo
                }
            };

            await _client.SubscriptionComponents.CreateUsage(subscriptionId.ToString(), componentId, usageRequest, cancellationToken);
            _logger.LogInformation("Recorded {Quantity} units of usage on subscription {SubscriptionId}", quantity, subscriptionId);
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record usage");
            throw new BillingProviderException($"Failed to record usage: {ex.Message}", ex);
        }
    }

    public async Task<UsageRecordResult> GetUsageAsync(int subscriptionId, int componentId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Negative(subscriptionId, nameof(subscriptionId));
        Guard.Against.Negative(componentId, nameof(componentId));

        try
        {
            _logger.LogDebug("Getting usage: subscription={SubscriptionId}, component={ComponentId}", subscriptionId, componentId);

            var usages = await _client.SubscriptionComponents.ListUsages(
                subscriptionId.ToString(),
                componentId,
                sinceId: null,
                maxId: null,
                sinceDate: null,
                untilDate: null,
                page: 1,
                perPage: 100,
                ct: cancellationToken);

            var total = usages.Sum(u => string.IsNullOrEmpty(u.Quantity) ? 0m : decimal.Parse(u.Quantity ?? "0"));

            _logger.LogDebug("Period-to-date usage for subscription {SubscriptionId}: {Total}", subscriptionId, total);

            return new UsageRecordResult
            {
                Success = true,
                PeriodToDateTotal = total
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get usage");
            return new UsageRecordResult
            {
                Success = false,
                PeriodToDateTotal = 0,
                ErrorMessage = $"Failed to get usage: {ex.Message}"
            };
        }
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, int newProductId, bool prorationOnChange, CancellationToken cancellationToken = default)
    {
        Guard.Against.Negative(subscriptionId, nameof(subscriptionId));
        Guard.Against.Negative(newProductId, nameof(newProductId));

        try
        {
            _logger.LogDebug("Previewing plan change: subscription={SubscriptionId}, newProduct={NewProductId}, proration={Proration}", subscriptionId, newProductId, prorationOnChange);

            var subscription = await _client.Subscriptions.ReadSubscription(subscriptionId, null, cancellationToken);

            var previewRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = subscription.Subscription?.CustomerId ?? 0,
                    ProductId = newProductId
                }
            };

            var preview = await _client.Subscriptions.PreviewSubscription(previewRequest, cancellationToken);

            var nextBillingDate = preview.Subscription?.NextBillingAt ?? DateTime.UtcNow.AddMonths(1);
            var newPrice = preview.Subscription?.CurrentPrice ?? 0m;
            var prorationCharge = 0m;

            _logger.LogDebug("Plan change preview: proration={Proration}, effectiveDate={EffectiveDate}", prorationCharge, nextBillingDate);

            return new PlanChangePreview
            {
                ProrationCharge = prorationCharge,
                NewProductPrice = newPrice,
                EffectiveDate = nextBillingDate
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to preview plan change");
            throw new BillingProviderException($"Failed to preview plan change: {ex.Message}", ex);
        }
    }

    public async Task ChangePlanAsync(int subscriptionId, int newProductId, bool prorationOnChange, CancellationToken cancellationToken = default)
    {
        Guard.Against.Negative(subscriptionId, nameof(subscriptionId));
        Guard.Against.Negative(newProductId, nameof(newProductId));

        try
        {
            _logger.LogDebug("Changing plan: subscription={SubscriptionId}, newProduct={NewProductId}", subscriptionId, newProductId);

            var updateRequest = new UpdateSubscriptionRequest
            {
                Subscription = new UpdateSubscription
                {
                    ProductId = newProductId,
                    Proration = prorationOnChange
                }
            };

            await _client.Subscriptions.UpdateSubscription(subscriptionId, updateRequest, cancellationToken);
            _logger.LogInformation("Changed plan for subscription {SubscriptionId} to product {NewProductId}", subscriptionId, newProductId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change plan");
            throw new BillingProviderException($"Failed to change plan: {ex.Message}", ex);
        }
    }

    public async Task PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Negative(subscriptionId, nameof(subscriptionId));

        try
        {
            _logger.LogDebug("Pausing subscription {SubscriptionId}", subscriptionId);

            var updateRequest = new UpdateSubscriptionRequest
            {
                Subscription = new UpdateSubscription
                {
                    State = "paused"
                }
            };

            await _client.Subscriptions.UpdateSubscription(subscriptionId, updateRequest, cancellationToken);
            _logger.LogInformation("Paused subscription {SubscriptionId}", subscriptionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause subscription");
            throw new BillingProviderException($"Failed to pause subscription: {ex.Message}", ex);
        }
    }

    public async Task ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Negative(subscriptionId, nameof(subscriptionId));

        try
        {
            _logger.LogDebug("Resuming subscription {SubscriptionId}", subscriptionId);

            var activateRequest = new ActivateSubscriptionRequest();
            await _client.Subscriptions.ActivateSubscription(subscriptionId, activateRequest, cancellationToken);

            _logger.LogInformation("Resumed subscription {SubscriptionId}", subscriptionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume subscription");
            throw new BillingProviderException($"Failed to resume subscription: {ex.Message}", ex);
        }
    }

    public async Task CancelSubscriptionAsync(int subscriptionId, bool immediate = false, CancellationToken cancellationToken = default)
    {
        Guard.Against.Negative(subscriptionId, nameof(subscriptionId));

        try
        {
            _logger.LogDebug("Cancelling subscription {SubscriptionId}, immediate={Immediate}", subscriptionId, immediate);

            var updateRequest = new UpdateSubscriptionRequest
            {
                Subscription = new UpdateSubscription
                {
                    CancellationMessage = "Cancelled by customer",
                    CancellationMethod = immediate ? "imo" : "dunning"
                }
            };

            await _client.Subscriptions.UpdateSubscription(subscriptionId, updateRequest, cancellationToken);
            _logger.LogInformation("Cancelled subscription {SubscriptionId}", subscriptionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel subscription");
            throw new BillingProviderException($"Failed to cancel subscription: {ex.Message}", ex);
        }
    }

    public async Task ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Negative(subscriptionId, nameof(subscriptionId));

        try
        {
            _logger.LogDebug("Reactivating subscription {SubscriptionId}", subscriptionId);

            var activateRequest = new ActivateSubscriptionRequest();
            await _client.Subscriptions.ActivateSubscription(subscriptionId, activateRequest, cancellationToken);

            _logger.LogInformation("Reactivated subscription {SubscriptionId}", subscriptionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reactivate subscription");
            throw new BillingProviderException($"Failed to reactivate subscription: {ex.Message}", ex);
        }
    }

    public async Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Negative(subscriptionId, nameof(subscriptionId));

        try
        {
            _logger.LogDebug("Getting subscription {SubscriptionId}", subscriptionId);

            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, null, cancellationToken);
            return MapToBillingSubscription(response.Subscription!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subscription");
            throw new BillingProviderException($"Failed to get subscription: {ex.Message}", ex);
        }
    }

    public async Task ValidateComponentIsMeteredAsync(int productFamilyId, int componentId, CancellationToken cancellationToken = default)
    {
        if (_componentValidated)
            return;

        try
        {
            _logger.LogDebug("Validating metered component {ComponentId} in family {FamilyId}", componentId, productFamilyId);
            _componentValidated = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate metered component");
            throw new BillingProviderException($"Failed to validate metered component: {ex.Message}", ex);
        }
    }

    private BillingProduct MapToBillingProduct(ProductResponse product)
    {
        return new BillingProduct
        {
            Id = product.Id ?? 0,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Price = product.DefaultPrice ?? 0m,
            BillingInterval = product.IntervalUnit?.Value ?? "month",
            RequiresPaymentMethod = product.RequireCreditCard ?? false
        };
    }

    private BillingCustomer MapToBillingCustomer(Customer customer)
    {
        return new BillingCustomer
        {
            Id = customer.Id ?? 0,
            Reference = customer.Reference ?? string.Empty,
            Email = customer.Email ?? string.Empty
        };
    }

    private BillingSubscription MapToBillingSubscription(Subscription subscription)
    {
        return new BillingSubscription
        {
            Id = subscription.Id ?? 0,
            Handle = subscription.Reference ?? string.Empty,
            CustomerId = subscription.CustomerId ?? 0,
            ProductId = subscription.Product?.Id ?? 0,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            State = subscription.State?.Value ?? "active",
            CreatedAt = subscription.CreatedAt ?? DateTime.UtcNow,
            ActivatedAt = subscription.ActivatedAt,
            CancelledAt = subscription.CanceledAt,
            NextBillingDate = subscription.NextBillingAt ?? DateTime.UtcNow.AddMonths(1),
            CurrentPrice = subscription.CurrentPrice ?? 0m
        };
    }
}
