using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioSubscriptionService
{
    Task<List<MaxioProduct>> GetPlansAsync();
    Task<MaxioSubscription> SubscribeAsync(string userId, string productHandle, string userEmail, string firstName, string lastName);
    Task<List<MaxioSubscription>> GetMySubscriptionsAsync(string userId);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    private static readonly ConcurrentDictionary<string, int> _userToMaxioCustomerMap = new();

    public MaxioSubscriptionService(
        IMaxioClient maxioClient,
        UserManager<ApplicationUser> userManager,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<MaxioProduct>> GetPlansAsync()
    {
        return await _maxioClient.ListProductsAsync(_options.ProductFamilyHandle);
    }

    public async Task<MaxioSubscription> SubscribeAsync(
        string userId,
        string productHandle,
        string userEmail,
        string firstName,
        string lastName)
    {
        // 1. Ensure Maxio customer exists (idempotent by reference)
        var maxioCustomerId = await EnsureCustomerAsync(userId, userEmail, firstName, lastName);

        // 2. Check if user already has an active subscription for this product
        var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(maxioCustomerId);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            s.Product?.Handle == productHandle &&
            s.State is "active" or "trialing" or "pending");
        if (existing != null)
        {
            _logger.LogInformation(
                "User {UserId} already has active subscription {SubId} for product {Handle}",
                userId, existing.Id, productHandle);
            return existing;
        }

        // 3. Create subscription
        var request = new CreateMaxioSubscriptionRequest
        {
            Subscription = new CreateMaxioSubscription
            {
                ProductHandle = productHandle,
                CustomerId = maxioCustomerId
            }
        };

        return await _maxioClient.CreateSubscriptionAsync(request);
    }

    public async Task<List<MaxioSubscription>> GetMySubscriptionsAsync(string userId)
    {
        var maxioCustomerId = await GetMaxioCustomerIdAsync(userId);
        if (maxioCustomerId == null)
            return new List<MaxioSubscription>();

        return await _maxioClient.ListCustomerSubscriptionsAsync(maxioCustomerId.Value);
    }

    private async Task<int> EnsureCustomerAsync(
        string userId,
        string userEmail,
        string firstName,
        string lastName)
    {
        // Check in-memory cache first
        if (_userToMaxioCustomerMap.TryGetValue(userId, out var cachedId))
            return cachedId;

        // Check Maxio by reference
        var existing = await _maxioClient.FindCustomerByReferenceAsync(userId);
        if (existing != null)
        {
            _userToMaxioCustomerMap[userId] = existing.Id;
            return existing.Id;
        }

        // Create new customer
        var createRequest = new CreateMaxioCustomerRequest
        {
            Customer = new CreateMaxioCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = userEmail,
                Reference = userId
            }
        };

        var created = await _maxioClient.CreateCustomerAsync(createRequest);
        _userToMaxioCustomerMap[userId] = created.Id;
        _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}", created.Id, userId);
        return created.Id;
    }

    private async Task<int?> GetMaxioCustomerIdAsync(string userId)
    {
        if (_userToMaxioCustomerMap.TryGetValue(userId, out var cachedId))
            return cachedId;

        var existing = await _maxioClient.FindCustomerByReferenceAsync(userId);
        if (existing != null)
        {
            _userToMaxioCustomerMap[userId] = existing.Id;
            return existing.Id;
        }

        return null;
    }
}
