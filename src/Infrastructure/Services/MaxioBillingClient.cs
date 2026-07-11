using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
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

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> options, ILogger<MaxioBillingClient> logger)
    {
        _settings = options.Value;
        _logger = logger;

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = _settings.ApiKey,
                Password = "x"
            },
            Environment = _settings.Environment?.ToUpperInvariant() == "EU"
                ? ServerEnvironment.Eu
                : ServerEnvironment.Us,
            Server = new ServerOptions()
        };

        var baseUrl = _settings.ResolveBaseUrl();
        if (!string.IsNullOrEmpty(baseUrl) && baseUrl != clientOptions.Server.Url)
        {
            clientOptions.Server.Url = baseUrl;
        }

        _client = new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }

    public async Task<BillingCustomer?> GetOrCreateCustomerAsync(string userEmail)
    {
        try
        {
            _logger.LogInformation("Looking up customer by reference: {Email}", userEmail);
            var response = await _client.Customers.ReadCustomerByReference(userEmail);
            var customer = response.Customer;

            return new BillingCustomer
            {
                Id = (int)customer.Id!,
                Email = customer.Email ?? string.Empty,
                FirstName = customer.FirstName ?? string.Empty,
                LastName = customer.LastName ?? string.Empty
            };
        }
        catch (Exception ex) when (ex.Message.Contains("404") || ex.Message.Contains("not found"))
        {
            _logger.LogInformation("Customer not found, creating new customer: {Email}", userEmail);

            try
            {
                var createRequest = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        Email = userEmail,
                        FirstName = userEmail.Split('@')[0],
                        LastName = "Subscriber",
                        Reference = userEmail
                    }
                };

                var createResponse = await _client.Customers.CreateCustomer(createRequest);
                var customer = createResponse.Customer;

                return new BillingCustomer
                {
                    Id = (int)customer.Id!,
                    Email = customer.Email ?? string.Empty,
                    FirstName = customer.FirstName ?? string.Empty,
                    LastName = customer.LastName ?? string.Empty
                };
            }
            catch (Exception createEx)
            {
                _logger.LogError(createEx, "Failed to create customer: {Email}", userEmail);
                throw new BillingProviderException($"Failed to create customer: {createEx.Message}", createEx);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get or create customer: {Email}", userEmail);
            throw new BillingProviderException($"Failed to get or create customer: {ex.Message}", ex);
        }
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, int productId)
    {
        try
        {
            _logger.LogInformation("Creating subscription for customer {CustomerId} on product {ProductId}", customerId, productId);

            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductId = productId
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(createRequest);
            var subscription = response.Subscription;

            return new BillingSubscription
            {
                Id = (int)subscription.Id!,
                CustomerId = (int)subscription.CustomerId!,
                ProductId = (int)subscription.ProductId!,
                State = subscription.State ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create subscription for customer {CustomerId}", customerId);
            throw new BillingProviderException($"Failed to create subscription: {ex.Message}", ex);
        }
    }

    public async Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId)
    {
        try
        {
            _logger.LogInformation("Reading subscription {SubscriptionId}", subscriptionId);

            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, null);
            var subscription = response.Subscription;

            return new BillingSubscription
            {
                Id = (int)subscription.Id!,
                CustomerId = (int)subscription.CustomerId!,
                ProductId = (int)subscription.ProductId!,
                State = subscription.State ?? string.Empty,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt.HasValue ? (decimal)subscription.CurrentPeriodEndsAt.Value.ToUnixTimeSeconds() : null,
                NextBillingAt = subscription.NextBillingAt.HasValue ? (decimal)subscription.NextBillingAt.Value.ToUnixTimeSeconds() : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read subscription {SubscriptionId}", subscriptionId);
            throw new BillingProviderException($"Failed to read subscription: {ex.Message}", ex);
        }
    }

    public async Task<List<BillingProduct>> ListProductsAsync(int productFamilyId)
    {
        try
        {
            _logger.LogInformation("Listing products for family {ProductFamilyId}", productFamilyId);

            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId.ToString(),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 100);

            return response
                .Select(p => new BillingProduct
                {
                    Id = (int)p.Id!,
                    FamilyId = (int)p.FamilyId!,
                    Name = p.Name ?? string.Empty,
                    Handle = p.ApiHandle ?? string.Empty,
                    Price = Convert.ToDecimal(p.PriceInCents ?? 0) / 100m,
                    PricingScheme = p.PricingScheme ?? string.Empty
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list products for family {ProductFamilyId}", productFamilyId);
            throw new BillingProviderException($"Failed to list products: {ex.Message}", ex);
        }
    }

    public async Task<BillingProduct> GetProductAsync(int productId)
    {
        try
        {
            _logger.LogInformation("Reading product {ProductId}", productId);

            var response = await _client.Products.ReadProduct(productId);
            var product = response.Product;

            return new BillingProduct
            {
                Id = (int)product.Id!,
                FamilyId = (int)product.FamilyId!,
                Name = product.Name ?? string.Empty,
                Handle = product.ApiHandle ?? string.Empty,
                Price = Convert.ToDecimal(product.PriceInCents ?? 0) / 100m,
                PricingScheme = product.PricingScheme ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read product {ProductId}", productId);
            throw new BillingProviderException($"Failed to read product: {ex.Message}", ex);
        }
    }

    public async Task<BillingComponent?> GetComponentByHandleAsync(int productFamilyId, string componentHandle)
    {
        try
        {
            _logger.LogInformation("Finding component by handle: {ComponentHandle}", componentHandle);

            var response = await _client.Components.FindComponent(componentHandle);
            var component = response.Component;

            if (component.ProductFamilyId != productFamilyId)
            {
                _logger.LogWarning("Component {Handle} is on family {ActualFamily}, not {ExpectedFamily}",
                    componentHandle, component.ProductFamilyId, productFamilyId);
                return null;
            }

            return new BillingComponent
            {
                Id = (int)component.Id!,
                ProductFamilyId = (int)component.ProductFamilyId!,
                Name = component.Name ?? string.Empty,
                Handle = component.Handle ?? string.Empty,
                Kind = component.ComponentType ?? string.Empty,
                Price = Convert.ToDecimal(component.PricePerUnitInCents ?? 0) / 100m
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find component by handle: {ComponentHandle}", componentHandle);
            return null;
        }
    }

    public async Task RecordUsageAsync(int subscriptionId, int componentId, decimal quantity, string? memo = null)
    {
        try
        {
            if (quantity <= 0)
            {
                throw new ArgumentException("Quantity must be greater than 0", nameof(quantity));
            }

            _logger.LogInformation("Recording usage for subscription {SubscriptionId}, component {ComponentId}, quantity {Quantity}",
                subscriptionId, componentId, quantity);

            var usageRequest = new CreateUsageRequest
            {
                Usage = new CreateUsage
                {
                    Quantity = (double)quantity,
                    MemoAttribute = memo
                }
            };

            var subscriptionRef = new SubscriptionIdOrReference { Value = subscriptionId.ToString() };
            var componentRef = new ComponentIdModel { Value = componentId.ToString() };

            await _client.SubscriptionComponents.CreateUsage(subscriptionRef, componentRef, usageRequest);

            _logger.LogInformation("Usage recorded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record usage");
            throw new BillingProviderException($"Failed to record usage: {ex.Message}", ex);
        }
    }

    public async Task<UsageData> GetUsageAsync(int subscriptionId, int componentId)
    {
        try
        {
            _logger.LogInformation("Reading usage for subscription {SubscriptionId}, component {ComponentId}", subscriptionId, componentId);

            var response = await _client.SubscriptionComponents.ReadSubscriptionComponentUsageMetadata(
                subscriptionId,
                componentId);

            var metadata = response.UsageMetadata ?? new UsageMetadata();
            var currentUsage = metadata.LastReset != null
                ? (decimal?)metadata.LastReset
                : 0m;

            var component = await GetProductAsync(componentId); // Get component price info

            return new UsageData
            {
                Id = componentId,
                CurrentUsage = currentUsage ?? 0m,
                UnitPrice = component.Price
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read usage");
            throw new BillingProviderException($"Failed to read usage: {ex.Message}", ex);
        }
    }

    public async Task<ChangeSubscriptionPlanPreview> PreviewPlanChangeAsync(int subscriptionId, int newProductId)
    {
        try
        {
            _logger.LogInformation("Previewing plan change for subscription {SubscriptionId} to product {ProductId}",
                subscriptionId, newProductId);

            var previewRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductId = newProductId
                }
            };

            var response = await _client.Subscriptions.PreviewSubscription(previewRequest);
            var preview = response.SubscriptionPreview;

            return new ChangeSubscriptionPlanPreview
            {
                HighestChargeInTermsOfStatusAmount = Convert.ToDecimal(preview.Credit ?? 0),
                LowestChargeInTermsOfStatusAmount = Convert.ToDecimal(preview.Credit ?? 0),
                AccruedProrationAdjustmentAmount = Convert.ToDecimal(preview.Credit ?? 0)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to preview plan change");
            throw new BillingProviderException($"Failed to preview plan change: {ex.Message}", ex);
        }
    }

    public async Task<BillingSubscription> ChangeSubscriptionPlanAsync(int subscriptionId, int newProductId)
    {
        try
        {
            _logger.LogInformation("Changing plan for subscription {SubscriptionId} to product {ProductId}",
                subscriptionId, newProductId);

            var updateRequest = new UpdateSubscriptionRequest
            {
                Subscription = new UpdateSubscription
                {
                    ProductId = newProductId
                }
            };

            var response = await _client.Subscriptions.UpdateSubscription(subscriptionId, updateRequest);
            var subscription = response.Subscription;

            return new BillingSubscription
            {
                Id = (int)subscription.Id!,
                CustomerId = (int)subscription.CustomerId!,
                ProductId = (int)subscription.ProductId!,
                State = subscription.State ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change subscription plan");
            throw new BillingProviderException($"Failed to change subscription plan: {ex.Message}", ex);
        }
    }

    public async Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId)
    {
        try
        {
            _logger.LogInformation("Pausing subscription {SubscriptionId}", subscriptionId);

            var updateRequest = new UpdateSubscriptionRequest
            {
                Subscription = new UpdateSubscription
                {
                    State = UpdateSubscription.StateEnum.Paused
                }
            };

            var response = await _client.Subscriptions.UpdateSubscription(subscriptionId, updateRequest);
            var subscription = response.Subscription;

            return new BillingSubscription
            {
                Id = (int)subscription.Id!,
                CustomerId = (int)subscription.CustomerId!,
                ProductId = (int)subscription.ProductId!,
                State = subscription.State ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause subscription");
            throw new BillingProviderException($"Failed to pause subscription: {ex.Message}", ex);
        }
    }

    public async Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId)
    {
        try
        {
            _logger.LogInformation("Resuming subscription {SubscriptionId}", subscriptionId);

            var response = await _client.Subscriptions.ResumeSubscription(subscriptionId, null);
            var subscription = response.Subscription;

            return new BillingSubscription
            {
                Id = (int)subscription.Id!,
                CustomerId = (int)subscription.CustomerId!,
                ProductId = (int)subscription.ProductId!,
                State = subscription.State ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume subscription");
            throw new BillingProviderException($"Failed to resume subscription: {ex.Message}", ex);
        }
    }

    public async Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, bool atEndOfPeriod = false)
    {
        try
        {
            _logger.LogInformation("Cancelling subscription {SubscriptionId}, atEndOfPeriod: {AtEndOfPeriod}",
                subscriptionId, atEndOfPeriod);

            var cancelRequest = new CancelSubscriptionRequest
            {
                Subscription = new CancelSubscription
                {
                    CancellationMessage = "Cancelled by customer"
                }
            };

            var response = await _client.Subscriptions.CancelSubscription(subscriptionId, cancelRequest, atEndOfPeriod);
            var subscription = response.Subscription;

            return new BillingSubscription
            {
                Id = (int)subscription.Id!,
                CustomerId = (int)subscription.CustomerId!,
                ProductId = (int)subscription.ProductId!,
                State = subscription.State ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel subscription");
            throw new BillingProviderException($"Failed to cancel subscription: {ex.Message}", ex);
        }
    }

    public async Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId)
    {
        try
        {
            _logger.LogInformation("Reactivating subscription {SubscriptionId}", subscriptionId);

            var response = await _client.Subscriptions.ActivateSubscription(subscriptionId, null);
            var subscription = response.Subscription;

            return new BillingSubscription
            {
                Id = (int)subscription.Id!,
                CustomerId = (int)subscription.CustomerId!,
                ProductId = (int)subscription.ProductId!,
                State = subscription.State ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reactivate subscription");
            throw new BillingProviderException($"Failed to reactivate subscription: {ex.Message}", ex);
        }
    }
}
