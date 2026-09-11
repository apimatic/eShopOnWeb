using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioApiClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionService(IMaxioApiClient maxioClient, UserManager<ApplicationUser> userManager)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userId, string productHandle)
    {
        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        var email = user.Email ?? $"{user.UserName}@placeholder.local";
        var firstName = user.UserName ?? "Unknown";
        var lastName = "User";

        // Idempotent customer: find or create by reference (userId)
        var customer = await _maxioClient.FindCustomerByReferenceAsync(userId);
        if (customer == null)
        {
            customer = await _maxioClient.CreateCustomerAsync(userId, email, firstName, lastName);
        }

        // Generate a deterministic uniqueness token from userId + productHandle
        // to prevent duplicate subscriptions on double-click
        var uniquenessToken = GenerateUniquenessToken(userId, productHandle);

        var subscription = await _maxioClient.CreateSubscriptionAsync(customer.Id, productHandle, uniquenessToken);

        return MapSubscription(subscription);
    }

    public async Task<List<SubscriptionDto>> GetMySubscriptionsAsync(string userId)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(userId);
        if (customer == null)
            return new List<SubscriptionDto>();

        var subscriptions = await _maxioClient.GetCustomerSubscriptionsAsync(customer.Id);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private static SubscriptionDto MapSubscription(Maxio.SubscriptionDto s) => new()
    {
        Id = s.Id,
        State = s.State,
        PriceInCents = s.PriceInCents,
        ProductHandle = s.ProductHandle,
        ProductName = s.ProductName,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        NextAssessmentAt = s.NextAssessmentAt,
        ActivatedAt = s.ActivatedAt,
        CanceledAt = s.CanceledAt,
        CreatedAt = s.CreatedAt
    };

    private static string GenerateUniquenessToken(string userId, string productHandle)
    {
        var input = $"{userId}:{productHandle}";
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}
