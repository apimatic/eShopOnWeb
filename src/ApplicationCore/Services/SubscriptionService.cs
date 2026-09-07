using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public interface ISubscriptionService
{
    Task<Result<SubscriptionDto>> CreateSubscriptionAsync(string userId, string firstName, string lastName, string email, string productHandle, CancellationToken cancellationToken = default);
    Task<Result<List<SubscriptionDto>>> GetUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioApiClient _maxioClient;
    private readonly IRepository<MaxioCustomer> _maxioCustomerRepository;
    private readonly IRepository<Subscription> _subscriptionRepository;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioApiClient maxioClient,
        IRepository<MaxioCustomer> maxioCustomerRepository,
        IRepository<Subscription> subscriptionRepository,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _maxioCustomerRepository = maxioCustomerRepository;
        _subscriptionRepository = subscriptionRepository;
        _logger = logger;
    }

    public async Task<Result<SubscriptionDto>> CreateSubscriptionAsync(
        string userId,
        string firstName,
        string lastName,
        string email,
        string productHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NullOrEmpty(firstName, nameof(firstName));
        Guard.Against.NullOrEmpty(lastName, nameof(lastName));
        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        try
        {
            // Get or create Maxio customer (idempotent)
            var maxioCustomer = await _maxioClient.GetOrCreateCustomerAsync(firstName, lastName, email, userId, cancellationToken);
            _logger.LogInformation("Maxio customer {CustomerId} ready for user {UserId}", maxioCustomer.Id, userId);

            // Store/update MaxioCustomer mapping
            var existingMapping = await _maxioCustomerRepository.FirstOrDefaultAsync(
                new MaxioCustomerByUserIdSpec(userId),
                cancellationToken);

            if (existingMapping == null)
            {
                var mapping = new MaxioCustomer(userId, maxioCustomer.Id, DateTime.UtcNow, DateTime.UtcNow);
                await _maxioCustomerRepository.AddAsync(mapping, cancellationToken);
                _logger.LogInformation("Created MaxioCustomer mapping for user {UserId}", userId);
            }
            else if (existingMapping.MaxioCustomerId != maxioCustomer.Id)
            {
                _logger.LogWarning("MaxioCustomer mismatch for user {UserId}: expected {ExpectedId}, got {ActualId}",
                    userId, existingMapping.MaxioCustomerId, maxioCustomer.Id);
                return Result.Error("Customer mapping conflict");
            }

            // Create subscription in Maxio
            var maxioSubscription = await _maxioClient.CreateSubscriptionAsync(maxioCustomer.Id, productHandle, cancellationToken);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for customer {CustomerId}", maxioSubscription.Id, maxioCustomer.Id);

            // Store subscription record
            var subscription = new Subscription(
                userId,
                maxioCustomer.Id,
                maxioSubscription.Id,
                productHandle,
                maxioSubscription.State,
                maxioSubscription.CreatedAt,
                maxioSubscription.UpdatedAt,
                maxioSubscription.NextAssessmentAt);

            await _subscriptionRepository.AddAsync(subscription, cancellationToken);
            _logger.LogInformation("Stored subscription record for user {UserId}", userId);

            return Result.Success(MapToDto(subscription, maxioSubscription));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create subscription for user {UserId}", userId);
            return Result.Error($"Failed to create subscription: {ex.Message}");
        }
    }

    public async Task<Result<List<SubscriptionDto>>> GetUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));

        try
        {
            var maxioCustomerMapping = await _maxioCustomerRepository.FirstOrDefaultAsync(
                new MaxioCustomerByUserIdSpec(userId),
                cancellationToken);

            if (maxioCustomerMapping == null)
            {
                _logger.LogInformation("No Maxio customer found for user {UserId}", userId);
                return Result.Success(new List<SubscriptionDto>());
            }

            var maxioSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(maxioCustomerMapping.MaxioCustomerId, cancellationToken);
            _logger.LogInformation("Retrieved {Count} subscriptions from Maxio for customer {CustomerId}", maxioSubscriptions.Length, maxioCustomerMapping.MaxioCustomerId);

            // Get local subscription records
            var localSubscriptions = await _subscriptionRepository.ListAsync(
                new SubscriptionsByUserIdSpec(userId),
                cancellationToken);

            var dtos = new List<SubscriptionDto>();
            foreach (var maxioSub in maxioSubscriptions)
            {
                var localSub = localSubscriptions.FirstOrDefault(s => s.MaxioSubscriptionId == maxioSub.Id);
                dtos.Add(MapToDto(localSub, maxioSub));
            }

            return Result.Success(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subscriptions for user {UserId}", userId);
            return Result.Error($"Failed to retrieve subscriptions: {ex.Message}");
        }
    }

    private static SubscriptionDto MapToDto(Subscription? localSubscription, MaxioSubscriptionResponse maxioSubscription)
    {
        return new SubscriptionDto
        {
            Id = localSubscription?.Id,
            MaxioSubscriptionId = maxioSubscription.Id,
            ProductHandle = localSubscription?.ProductHandle ?? (maxioSubscription.Product?.Handle ?? "unknown"),
            ProductName = maxioSubscription.Product?.Name ?? "Unknown Product",
            State = maxioSubscription.State,
            PriceInCents = maxioSubscription.Product?.PriceInCents ?? 0,
            NextBillingAt = maxioSubscription.NextAssessmentAt,
            CreatedAt = maxioSubscription.CreatedAt
        };
    }
}

public class SubscriptionDto
{
    public int? Id { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Specifications for queries
public class MaxioCustomerByUserIdSpec : Ardalis.Specification.Specification<MaxioCustomer>
{
    public MaxioCustomerByUserIdSpec(string userId)
    {
        Query.Where(m => m.UserId == userId);
    }
}

public class SubscriptionsByUserIdSpec : Ardalis.Specification.Specification<Subscription>
{
    public SubscriptionsByUserIdSpec(string userId)
    {
        Query.Where(s => s.UserId == userId);
    }
}
