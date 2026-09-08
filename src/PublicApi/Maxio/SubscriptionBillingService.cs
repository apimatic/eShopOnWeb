using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Default <see cref="ISubscriptionBillingService"/> implementation backed by Maxio Advanced Billing.
///
/// The Maxio customer is the billing-system-of-record record for an eShopOnWeb user. A customer's
/// <c>reference</c> is derived deterministically from the user's (lower-cased) email, which is what makes
/// customer creation idempotent across restarts and double-clicks without a local persistence dependency:
/// look the customer up by reference first, create only when missing, and fall back to a lookup if a
/// concurrent create loses the uniqueness race.
///
/// Subscription creation is guarded by a per-user semaphore and by a pre-flight check of the customer's
/// existing subscriptions so that two concurrent subscribe requests can never yield two Maxio subscriptions
/// for the same plan.
/// </summary>
public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserGates = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired"
    };

    private readonly MaxioApiClient _client;
    private readonly string _productFamilyHandle;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        MaxioApiClient client,
        IOptions<MaxioOptions> options,
        ILogger<SubscriptionBillingService> logger)
    {
        _client = client;
        _productFamilyHandle = options.Value.ProductFamilyHandle;
        _logger = logger;
    }

    public Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken)
    {
        return _client.ListPlansAsync(cancellationToken);
    }

    public async Task<SubscribeResult> EnsureSubscriptionAsync(
        string userEmail,
        string planHandle,
        CancellationToken cancellationToken)
    {
        string email = NormalizeEmail(userEmail);
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        SemaphoreSlim gate = UserGates.GetOrAdd(email, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string reference = BuildCustomerReference(email);
            MaxioCustomer? customer = await _client.FindCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
            customer ??= await CreateCustomerAsync(email, reference, cancellationToken).ConfigureAwait(false);

            IReadOnlyList<MaxioSubscription> subscriptions =
                await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);

            MaxioSubscription? existing = subscriptions.FirstOrDefault(
                s => string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
                     && !IsTerminal(s.State));

            if (existing is not null)
            {
                _logger.LogInformation("User {Email} already subscribed to plan {PlanHandle} (Maxio subscription {SubscriptionId}). Reusing.",
                    email, planHandle, existing.Id);
                return new SubscribeResult(existing, created: false);
            }

            MaxioProduct plan = await GetPlanByHandleAsync(planHandle, cancellationToken).ConfigureAwait(false);
            DateTimeOffset nextBillingAt = ComputeNextBillingAt(plan, DateTimeOffset.UtcNow);

            MaxioSubscription created =
                await _client.CreateSubscriptionAsync(plan.Handle!, reference, nextBillingAt, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {Email} on plan {PlanHandle}.",
                created.Id, email, planHandle);
            return new SubscribeResult(created, created: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(
        string userEmail,
        CancellationToken cancellationToken)
    {
        string email = NormalizeEmail(userEmail);
        string reference = BuildCustomerReference(email);

        MaxioCustomer? customer = await _client.FindCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MaxioProduct> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken)
    {
        IReadOnlyList<MaxioProduct> plans = await _client.ListPlansAsync(cancellationToken).ConfigureAwait(false);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(planHandle, _productFamilyHandle);
    }

    private async Task<MaxioCustomer> CreateCustomerAsync(string email, string reference, CancellationToken cancellationToken)
    {
        (string firstName, string lastName) = DeriveDisplayName(email);
        var customer = new MaxioCustomer
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Reference = reference
        };

        try
        {
            return await _client.CreateCustomerAsync(customer, cancellationToken).ConfigureAwait(false);
        }
        catch (MaxioApiException ex)
            when (ex.IsClientError && ex.Message.Contains("reference", StringComparison.OrdinalIgnoreCase)
                                    && ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase))
        {
            // A concurrent request created the customer first (Maxio enforces a unique reference).
            // Reuse that customer instead of failing.
            _logger.LogInformation("Maxio customer create raced for reference {Reference}; looking the customer up.", reference);
            return await _client.FindCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false)
                ?? throw new MaxioApiException(ex.StatusCode, "The Maxio customer could neither be created nor found.", ex.ResponseBody);
        }
    }

    private static bool IsTerminal(string? state)
    {
        return state is not null && TerminalStates.Contains(state);
    }

    private static DateTimeOffset ComputeNextBillingAt(MaxioProduct plan, DateTimeOffset now)
    {
        int interval = plan.Interval > 0 ? plan.Interval : 1;
        return (plan.IntervalUnit ?? "month").ToLowerInvariant() switch
        {
            "day" => now.AddDays(interval),
            "week" => now.AddDays(7 * interval),
            "year" => now.AddYears(interval),
            _ => now.AddMonths(interval)
        };
    }

    private static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("An eShopOnWeb user identity (email) is required.", nameof(email));
        }

        return email.Trim().ToLowerInvariant();
    }

    private static string BuildCustomerReference(string normalizedEmail) => $"eshop-{normalizedEmail}";

    private static (string FirstName, string LastName) DeriveDisplayName(string email)
    {
        // eShopOnWeb identities only carry an email (the seed users use their email as username),
        // but Maxio requires a first and last name. Derive a sensible display name from the local part.
        string local = email.Split('@')[0];
        string[] parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        string first = parts.Length > 0 ? parts[0] : "eShop";
        string last = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "Member";
        return (Capitalize(first), Capitalize(last));
    }

    private static string Capitalize(string value)
    {
        return value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);
    }
}
