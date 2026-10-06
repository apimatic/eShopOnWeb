using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Billing.Contracts;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionService : ISubscriptionService
{
    private const string NoCardCollectionMethod = "remittance";

    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create"
    };

    private readonly IMaxioClient _maxioClient;
    private readonly IKeyedLock _keyedLock;
    private readonly MaxioSettings _settings;

    public SubscriptionService(IMaxioClient maxioClient, IKeyedLock keyedLock, MaxioSettings settings)
    {
        _maxioClient = maxioClient;
        _keyedLock = keyedLock;
        _settings = settings;
    }

    public async Task<IReadOnlyList<MaxioPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var handle = _settings.RequireProductFamilyHandle();
        return await _maxioClient.ListPlansForProductFamilyAsync(handle, cancellationToken);
    }

    public async Task<SubscriptionProvisionResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.UserKey))
        {
            throw new ArgumentException("A caller identity is required to subscribe.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.PlanHandle))
        {
            throw new ArgumentException("A plan handle is required to subscribe.", nameof(command));
        }

        var userKey = command.UserKey.Trim();
        var planHandle = command.PlanHandle.Trim();

        using var _ = await _keyedLock.AcquireWaitAsync($"eshop-user:{userKey}", cancellationToken);

        var customer = await EnsureCustomerAsync(command, userKey, cancellationToken);

        var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var alreadyActive = existingSubscriptions
            .FirstOrDefault(sub => IsLive(sub.State)
                && string.Equals(sub.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase));

        if (alreadyActive is not null)
        {
            return new SubscriptionProvisionResult(customer, alreadyActive, Created: false);
        }

        var subscription = await _maxioClient.CreateSubscriptionAsync(
            new CreateSubscriptionCommand(planHandle, customer.Id, NoCardCollectionMethod),
            cancellationToken);

        return new SubscriptionProvisionResult(customer, subscription, Created: true);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForUserAsync(string userKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userKey))
        {
            return Array.Empty<MaxioSubscription>();
        }

        var customer = await _maxioClient.FindCustomerByReferenceAsync(userKey.Trim(), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscribeCommand command, string userKey, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(userKey, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = string.IsNullOrWhiteSpace(command.Email) ? userKey : command.Email!;
        var firstName = ResolveFirstName(command.FirstName, email);
        var lastName = ResolveLastName(command.LastName);

        try
        {
            return await _maxioClient.CreateCustomerAsync(
                new CreateCustomerCommand(userKey, firstName, lastName, email),
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.IsUnprocessableEntity)
        {
            var recovered = await _maxioClient.FindCustomerByReferenceAsync(userKey, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw;
        }
    }

    private static bool IsLive(string state) => !TerminalStates.Contains(state);

    private static string ResolveFirstName(string? provided, string email)
    {
        if (!string.IsNullOrWhiteSpace(provided))
        {
            return provided!;
        }

        var localPart = email.Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
    }

    private static string ResolveLastName(string? provided)
        => string.IsNullOrWhiteSpace(provided) ? "Customer" : provided!;
}
