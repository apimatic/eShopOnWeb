using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Requests.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Ensures exactly one Maxio customer exists per eShopOnWeb user.
/// Order of every create: claim (primary-key insert of <see cref="BillingCustomerLink"/>) → Maxio call → record the result.
/// The customer reference is derived from the user name, so it is stable across restarts and Maxio refuses a second
/// customer with it; an unknown create outcome is settled by reading the customer back by that reference.
/// </summary>
public class MaxioCustomerProvisioner
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<MaxioCustomerProvisioner> _logger;

    public MaxioCustomerProvisioner(MaxioAdvancedBillingClient client, CatalogContext db, TimeProvider clock,
        ILogger<MaxioCustomerProvisioner> logger)
    {
        _client = client;
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>The key under which a user's billing claims are stored (Identity-style normalized user name).</summary>
    public static string NormalizeUserName(string userName) => userName.Trim().ToUpperInvariant();

    /// <summary>Deterministic, PII-free Maxio customer reference for a user.</summary>
    public static string CustomerReferenceFor(string userName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeUserName(userName)));
        return "eshop-" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    /// <summary>Returns the user's Maxio customer id, creating the customer if it does not exist yet.</summary>
    public async Task<int> EnsureCustomerAsync(string userName, CancellationToken cancellationToken)
    {
        var email = BillingEmailFor(userName);
        var (link, owned) = await ClaimAsync(userName, cancellationToken);
        if (!owned)
        {
            return link.ProviderCustomerId!.Value;
        }

        int customerId;
        try
        {
            customerId = await LookupAsync(link.CustomerReference, cancellationToken)
                         ?? await CreateAsync(link.CustomerReference, email, cancellationToken);
        }
        catch (BillingOutcomeUnknownException)
        {
            link.MarkUnknown(_clock.GetUtcNow());
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch
        {
            await ReleaseAsync(link);
            throw;
        }

        link.Complete(customerId, _clock.GetUtcNow());
        await _db.SaveChangesAsync(CancellationToken.None);
        return customerId;
    }

    /// <summary>Finds the user's Maxio customer id without creating one; null when the user has no customer yet.</summary>
    public async Task<int?> FindCustomerIdAsync(string userName, CancellationToken cancellationToken)
    {
        var key = NormalizeUserName(userName);
        var link = await _db.BillingCustomerLinks.AsNoTracking()
            .SingleOrDefaultAsync(l => l.UserName == key, cancellationToken);
        if (link is { Status: BillingClaimStatus.Completed, ProviderCustomerId: not null })
        {
            return link.ProviderCustomerId;
        }
        return await LookupAsync(CustomerReferenceFor(userName), cancellationToken);
    }

    private async Task<(BillingCustomerLink Link, bool Owned)> ClaimAsync(string userName, CancellationToken cancellationToken)
    {
        var key = NormalizeUserName(userName);
        var now = _clock.GetUtcNow();
        var claim = new BillingCustomerLink(key, CustomerReferenceFor(userName), now);
        _db.BillingCustomerLinks.Add(claim);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return (claim, true);
        }
        catch (Exception ex) when (ClaimStore.IsDuplicateKey(ex))
        {
            // The primary key refused a second link for this user: another request holds or completed the claim.
            _db.Entry(claim).State = EntityState.Detached;
        }

        var existing = await _db.BillingCustomerLinks.SingleOrDefaultAsync(l => l.UserName == key, cancellationToken);
        if (existing is null)
        {
            throw new SubscriptionInProgressException("Your billing account is being set up. Please try again.");
        }
        if (existing is { Status: BillingClaimStatus.Completed, ProviderCustomerId: not null })
        {
            return (existing, false);
        }
        if (existing.Status == BillingClaimStatus.Pending && now - existing.UpdatedAt < MaxioBillingLimits.StaleClaimAfter)
        {
            throw new SubscriptionInProgressException("Your billing account is being set up. Please try again shortly.");
        }

        // Unknown or stale Pending: take it over (optimistic concurrency lets exactly one caller win) and settle it
        // by looking the customer up by reference before any create.
        existing.TakeOver(now);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.Entry(existing).State = EntityState.Detached;
            throw new SubscriptionInProgressException("Your billing account is being set up. Please try again shortly.");
        }
        return (existing, true);
    }

    private async Task<int> CreateAsync(string reference, string email, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = FirstNameFor(email),
                            LastName = "eShopOnWeb",
                            Email = email,
                            Reference = reference
                        }
                    }
                },
                cancellationToken: cancellationToken);

            if (response.Customer.Id is int id)
            {
                _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.", id, reference);
                return id;
            }
            _logger.LogWarning("Maxio created customer {CustomerReference} but returned no id; reading it back.", reference);
            return await SettleUnknownCreateAsync(reference, cause: null);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                // A customer with this reference may already exist (an earlier attempt landed); Maxio refuses the
                // duplicate, and the existing customer is the answer.
                var existing = await LookupWithFreshBudgetAsync(reference);
                if (existing is int id)
                {
                    _logger.LogInformation("Maxio customer {CustomerId} already existed for reference {CustomerReference}.", id, reference);
                    return id;
                }
                var reasons = ReasonsFrom(body);
                _logger.LogWarning("Maxio refused to create customer {CustomerReference} (HTTP {Status}): {Reasons}",
                    reference, (int)ex.StatusCode, string.Join("; ", reasons));
                throw new BillingProviderException(BillingFailureKind.Rejected,
                    "The billing provider could not create a billing account for this user.", (int)ex.StatusCode, reasons, ex);
            }
            if (ex.Error.TryGetRawError(out RawError raw))
            {
                if (MaxioFailures.IsAmbiguousWriteStatus(raw.StatusCode))
                {
                    return await SettleUnknownCreateAsync(reference, ex);
                }
                _logger.LogWarning("Maxio refused to create customer {CustomerReference} with HTTP {Status}.", reference, (int)raw.StatusCode);
                throw MaxioFailures.FromStatus(raw.StatusCode, ex, callerSuppliedInput: true);
            }
            throw MaxioFailures.FromStatus(ex.StatusCode, ex, callerSuppliedInput: true);
        }
        catch (ResponseDeserializationException ex) when (MaxioFailures.IsAmbiguousWriteStatus(ex.StatusCode))
        {
            return await SettleUnknownCreateAsync(reference, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            _logger.LogWarning(ex, "Maxio refused to create customer {CustomerReference} with an unreadable HTTP {Status} body.",
                reference, (int)ex.StatusCode);
            throw MaxioFailures.FromStatus(ex.StatusCode, ex, callerSuppliedInput: true);
        }
        catch (SdkConnectionException ex)
        {
            return await SettleUnknownCreateAsync(reference, ex);
        }
        catch (OperationCanceledException ex)
        {
            return await SettleUnknownCreateAsync(reference, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials could not be applied.");
            throw MaxioFailures.FromSdk(ex);
        }
    }

    /// <summary>
    /// The create may have landed: re-read by the reference that was sent. Found → the create succeeded.
    /// Definitively absent → it did not (the caller's claim is released). Re-read failed → outcome stays unknown.
    /// </summary>
    private async Task<int> SettleUnknownCreateAsync(string reference, Exception? cause)
    {
        _logger.LogWarning(cause, "Outcome of creating Maxio customer {CustomerReference} is unknown; reading it back by reference.", reference);
        int? found;
        try
        {
            found = await LookupWithFreshBudgetAsync(reference);
        }
        catch (Exception readFailure) when (readFailure is BillingProviderException or OperationCanceledException)
        {
            _logger.LogError(readFailure, "Could not settle the outcome of creating Maxio customer {CustomerReference}.", reference);
            throw new BillingOutcomeUnknownException(
                "Your billing account could not be confirmed yet. Please try again shortly.", cause ?? readFailure);
        }

        if (found is int id)
        {
            _logger.LogInformation("Settled: Maxio customer {CustomerId} was created for reference {CustomerReference}.", id, reference);
            return id;
        }
        _logger.LogWarning("Settled: Maxio customer {CustomerReference} was not created.", reference);
        throw new BillingProviderException(BillingFailureKind.Unavailable,
            "The billing provider did not create your billing account. Please try again.", innerException: cause);
    }

    private async Task<int?> LookupWithFreshBudgetAsync(string reference)
    {
        // Independent of the caller's token: the request budget may be what just expired.
        using var cts = new CancellationTokenSource(MaxioBillingLimits.ReconcileBudget);
        return await LookupAsync(reference, cts.Token);
    }

    /// <summary>Reads a customer by reference; null only for a 404 (never for an unreadable answer).</summary>
    private async Task<int?> LookupAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: cancellationToken);
            return response.Customer.Id
                   ?? throw new BillingProviderException(BillingFailureKind.Unavailable,
                       "The billing provider returned a response that could not be processed.");
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (ApiException<RawError> ex)
        {
            _logger.LogError("Looking up Maxio customer {CustomerReference} failed with HTTP {Status}.", reference, (int)ex.StatusCode);
            throw MaxioFailures.FromStatus(ex.StatusCode, ex);
        }
        catch (SdkException ex)
        {
            _logger.LogError(ex, "Looking up Maxio customer {CustomerReference} failed.", reference);
            throw MaxioFailures.FromSdk(ex);
        }
    }

    private async Task ReleaseAsync(BillingCustomerLink link)
    {
        try
        {
            _db.BillingCustomerLinks.Remove(link);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex)
        {
            _db.Entry(link).State = EntityState.Detached;
            _logger.LogWarning(ex, "Could not release the billing customer claim for reference {CustomerReference}.", link.CustomerReference);
        }
    }

    private static IReadOnlyList<string> ReasonsFrom(CustomerErrorResponse1 body)
    {
        if (body.Errors is null)
        {
            return Array.Empty<string>();
        }
        if (body.Errors.TryGetCustomerError(out var single) && !string.IsNullOrWhiteSpace(single.Customer))
        {
            return new[] { single.Customer! };
        }
        if (body.Errors.TryGetListOfString(out var list))
        {
            return list.Where(r => !string.IsNullOrWhiteSpace(r)).ToArray();
        }
        return Array.Empty<string>();
    }

    private static string BillingEmailFor(string userName)
    {
        var candidate = userName.Trim().ToLowerInvariant();
        if (MailAddress.TryCreate(candidate, out var address) && address.Address == candidate)
        {
            return candidate;
        }
        throw new BillingProviderException(BillingFailureKind.Rejected,
            "This account has no valid email address, which is required for a billing account.");
    }

    private static string FirstNameFor(string email)
    {
        var local = email.Split('@')[0];
        return local.Length > 50 ? local[..50] : local;
    }
}
