using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements subscription billing with Maxio Advanced Billing as the system of record.
///
/// Idempotency:
///  - Maxio customers are keyed on the eShopOnWeb user id via the customer `reference`
///    field, so repeated enrollments reuse the same Maxio customer.
///  - The first enrollment attempt for (user, plan) uses a deterministic subscription
///    `reference`; if a parallel attempt races it to Maxio, the duplicate create fails
///    with a unique-reference error and is reconciled by adopting the winning
///    subscription instead of creating a second one.
///  - Enrollment attempts are also journaled locally in SubscriptionRecord, so a
///    double-click that arrives sequentially replays the existing subscription.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly string[] TerminalStates = { "canceled", "expired", "ceased" };

    private readonly IMaxioClient _client;
    private readonly IRepository<SubscriptionRecord> _repository;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioClient client,
        IRepository<SubscriptionRecord> repository,
        IOptions<MaxioOptions> options,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _repository = repository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set MAXIO_DEFAULT_PRODUCT_FAMILY (or Maxio:ProductFamilyHandle).");

        var products = await _client.ListProductsAsync(cancellationToken);
        return products
            .Where(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.PriceInCents)
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval ?? 1,
                IntervalUnit = p.IntervalUnit,
                ProductFamilyHandle = p.ProductFamily.Handle
            })
            .ToList();
    }

    public async Task<SubscriptionSummary?> SubscribeAsync(
        string userId, string userName, string email, string productHandle,
        CancellationToken cancellationToken = default)
    {
        var product = await _client.GetProductByHandleAsync(productHandle, cancellationToken);
        if (product is null)
        {
            _logger.LogWarning($"Subscribe requested for unknown Maxio product handle '{productHandle}'.");
            return null;
        }

        var records = await _repository.ListAsync(new SubscriptionsForUserSpec(userId), cancellationToken);

        // Replay an existing live enrollment for this plan (double-click / repeat subscribe).
        var existing = records
            .Where(r => r.ProductHandle == productHandle
                        && r.MaxioSubscriptionId > 0
                        && !IsTerminal(r.State))
            .OrderByDescending(r => r.Id)
            .FirstOrDefault();
        if (existing is not null)
        {
            var live = await _client.GetSubscriptionAsync(existing.MaxioSubscriptionId, cancellationToken);
            if (live is null || IsTerminal(live.State))
            {
                // The subscription ended on the Maxio side; fall through and re-enroll.
                if (live is not null)
                    UpdateRecordFromLive(existing, live);
                else
                    existing.State = "canceled";
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await _repository.UpdateAsync(existing, cancellationToken);
            }
            else
            {
                UpdateRecordFromLive(existing, live);
                await _repository.UpdateAsync(existing, cancellationToken);
                _logger.LogInformation($"User '{userId}' replayed existing Maxio subscription {live.Id} for plan '{productHandle}'.");
                return ToSummary(existing);
            }
        }

        // Ensure a Maxio customer exists for this user (idempotent via customer reference).
        var customerReference = GetCustomerReference(userId);
        var customer = await _client.LookupCustomerByReferenceAsync(customerReference, cancellationToken);

        // Journal the enrollment intent before talking to Maxio.
        var priorAttempts = records.Where(r => r.ProductHandle == productHandle).ToList();
        var subscriptionReference = priorAttempts.Count == 0
            ? GetSubscriptionReference(userId, productHandle)
            : GetSubscriptionReference(userId, productHandle) + ":" + Guid.NewGuid().ToString("N").Substring(0, 8);

        var record = new SubscriptionRecord
        {
            UserId = userId,
            MaxioCustomerId = customer?.Id ?? 0,
            MaxioSubscriptionId = 0,
            MaxioReference = subscriptionReference,
            ProductHandle = product.Handle,
            ProductName = product.Name,
            State = "pending",
            PriceInCents = product.PriceInCents,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _repository.AddAsync(record, cancellationToken);

        var request = new MaxioCreateSubscriptionRequest
        {
            ProductHandle = product.Handle,
            PaymentCollectionMethod = "remittance",
            Reference = subscriptionReference
        };
        if (customer is not null)
        {
            request.CustomerId = customer.Id;
        }
        else
        {
            var (firstName, lastName) = DeriveCustomerName(userName, email);
            request.CustomerAttributes = new MaxioCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = customerReference
            };
        }

        MaxioSubscription subscription;
        try
        {
            subscription = await _client.CreateSubscriptionAsync(request, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422 && IsUniqueReferenceError(ex))
        {
            // A concurrent enrollment for the same user (and possibly same plan) won the race
            // to Maxio. Reconcile: adopt the winning subscription, or retry once against the
            // now-existing customer.
            _logger.LogWarning($"Maxio rejected subscription create with a duplicate reference for user '{userId}'. Reconciling.");

            subscription = await ReconcileAfterUniqueReferenceConflictAsync(
                request, customerReference, subscriptionReference, cancellationToken);

            if (subscription is null)
            {
                var resolvedCustomer = await _client.LookupCustomerByReferenceAsync(customerReference, cancellationToken);
                if (resolvedCustomer is null)
                    throw;
                request.CustomerAttributes = null;
                request.CustomerId = resolvedCustomer.Id;
                request.Reference = subscriptionReference + ":" + Guid.NewGuid().ToString("N").Substring(0, 8);
                record.MaxioReference = request.Reference;
                record.MaxioCustomerId = resolvedCustomer.Id;
                record.UpdatedAt = DateTimeOffset.UtcNow;
                await _repository.UpdateAsync(record, cancellationToken);
                subscription = await _client.CreateSubscriptionAsync(request, cancellationToken);
            }
        }
        catch (MaxioApiException)
        {
            await _repository.DeleteAsync(record, cancellationToken);
            throw;
        }
        catch (InvalidOperationException)
        {
            await _repository.DeleteAsync(record, cancellationToken);
            throw;
        }

        UpdateRecordFromLive(record, subscription);
        record.UpdatedAt = DateTimeOffset.UtcNow;
        await _repository.UpdateAsync(record, cancellationToken);
        _logger.LogInformation($"User '{userId}' subscribed to plan '{product.Handle}' as Maxio subscription {subscription.Id}.");
        return ToSummary(record);
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> ListForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var records = await _repository.ListAsync(new SubscriptionsForUserSpec(userId), cancellationToken);
        var summaries = new List<SubscriptionSummary>();
        foreach (var record in records)
        {
            if (record.MaxioSubscriptionId <= 0)
                continue; // never-completed enrollment attempt

            var live = await _client.GetSubscriptionAsync(record.MaxioSubscriptionId, cancellationToken);
            if (live is not null)
            {
                UpdateRecordFromLive(record, live);
                record.UpdatedAt = DateTimeOffset.UtcNow;
                await _repository.UpdateAsync(record, cancellationToken);
            }

            summaries.Add(ToSummary(record));
        }
        return summaries;
    }

    /// <summary>
    /// Handles the 422 unique-reference race: if a live subscription already exists under the
    /// contested reference, return it; otherwise return null so the caller can retry once.
    /// </summary>
    private async Task<MaxioSubscription?> ReconcileAfterUniqueReferenceConflictAsync(
        MaxioCreateSubscriptionRequest request, string customerReference, string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var candidates = await _client.ListSubscriptionsByReferenceAsync(subscriptionReference, cancellationToken);
        var winner = candidates.FirstOrDefault(s => !IsTerminal(s.State));
        if (winner is not null)
        {
            if (request.CustomerAttributes is not null)
            {
                // The conflicting request may also have created our customer; adopt it if so.
                var racedCustomer = await _client.LookupCustomerByReferenceAsync(customerReference, cancellationToken);
                if (racedCustomer is not null)
                {
                    request.CustomerAttributes = null;
                    request.CustomerId = racedCustomer.Id;
                }
            }
            return winner;
        }
        return null;
    }

    private static bool IsUniqueReferenceError(MaxioApiException ex) =>
        ex.Errors.Any(e => e.Contains("unique", StringComparison.OrdinalIgnoreCase)
                           && e.Contains("reference", StringComparison.OrdinalIgnoreCase));

    private static void UpdateRecordFromLive(SubscriptionRecord record, MaxioSubscription live)
    {
        record.MaxioSubscriptionId = live.Id;
        if (live.Customer is not null)
            record.MaxioCustomerId = live.Customer.Id;
        record.State = live.State;
        record.ProductName = live.Product?.Name ?? record.ProductName;
        record.PriceInCents = live.Product?.PriceInCents ?? (live.ProductPriceInCents > 0 ? live.ProductPriceInCents : record.PriceInCents);
        record.NextBillingDate = live.NextAssessmentAt;
        record.ActivatedAt = live.ActivatedAt;
    }

    private static SubscriptionSummary ToSummary(SubscriptionRecord record) => new()
    {
        MaxioSubscriptionId = record.MaxioSubscriptionId,
        MaxioCustomerId = record.MaxioCustomerId,
        MaxioReference = record.MaxioReference,
        ProductHandle = record.ProductHandle,
        ProductName = record.ProductName,
        PriceInCents = record.PriceInCents,
        State = record.State,
        ActivatedAt = record.ActivatedAt,
        NextBillingDate = record.NextBillingDate
    };

    private static string GetCustomerReference(string userId) => $"eshopuser-{userId}";

    private static string GetSubscriptionReference(string userId, string productHandle) =>
        $"eshopsub-{userId}-{productHandle}";

    private static bool IsTerminal(string state) =>
        TerminalStates.Contains(state, StringComparer.OrdinalIgnoreCase);

    private static (string FirstName, string LastName) DeriveCustomerName(string userName, string email)
    {
        var source = email ?? userName ?? string.Empty;
        var localPart = source.Contains('@') ? source.Split('@')[0] : source;
        var segments = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = Capitalize(segments.Length > 0 ? segments[0] : "eShop");
        var lastName = Capitalize(segments.Length > 1 ? segments[segments.Length - 1] : "Customer");
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
}
