using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Ensures exactly one Maxio customer per eShop user: claim (store PK) -> look up by reference -> create ->
/// record. The Maxio customer reference is the eShop user name.
/// </summary>
public class MaxioCustomerService
{
    /// <summary>A Pending claim younger than this is in flight; older ones are taken over and settled.</summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    private const string LastNameForShopUsers = "eShop";

    private readonly CatalogContext _db;
    private readonly MaxioGateway _gateway;
    private readonly TimeProvider _time;
    private readonly ILogger<MaxioCustomerService> _logger;

    public MaxioCustomerService(CatalogContext db, MaxioGateway gateway, TimeProvider time, ILogger<MaxioCustomerService> logger)
    {
        _db = db;
        _gateway = gateway;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Returns the Maxio customer id for <paramref name="userName"/>, creating the customer when none exists.
    /// Returns <see cref="CustomerOutcome.Pending"/> when a create's outcome could not be confirmed yet.
    /// Throws <see cref="DuplicateException"/> while another request holds the claim.
    /// </summary>
    public virtual async Task<CustomerOutcome> EnsureCustomerAsync(string userName, string email, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var claim = new MaxioCustomerLink(userName, now);

        // 1. Claim. The store refuses a second claim for the same user name (primary key).
        var existing = await BillingClaimStore.InsertOrGetExistingAsync(_db, claim,
            token => _db.MaxioCustomerLinks.SingleOrDefaultAsync(l => l.UserName == userName, token), ct);

        if (existing is not null)
        {
            switch (existing.Status)
            {
                case BillingClaimStatus.Completed when existing.MaxioCustomerId is not null:
                    return CustomerOutcome.Ready(existing.MaxioCustomerId.Value);

                case BillingClaimStatus.Pending when now - existing.ClaimedAtUtc < ClaimLease:
                    throw new DuplicateException("Your billing account is already being set up. Try again in a moment.");

                default:
                    // Unknown, or Pending past its lease (the holder died): take the claim over, then settle it by
                    // re-reading before any create. The concurrency token refuses a second taker.
                    existing.TakeOver(now);
                    if (!await BillingClaimStore.TrySaveAsync(_db, existing, _logger, ct))
                    {
                        throw new DuplicateException("Your billing account is already being set up. Try again in a moment.");
                    }
                    _logger.LogInformation("Took over an unsettled Maxio customer claim; settling it by reference.");
                    claim = existing;
                    break;
            }
        }
        else
        {
            _logger.LogInformation("Claimed Maxio customer creation for the current user.");
        }

        // 2. Look up by reference: adopt a customer created earlier (another run, a lost local store).
        MaxioCustomerRecord? found;
        try
        {
            found = await _gateway.FindCustomerByReferenceAsync(userName, ct);
        }
        catch
        {
            await BillingClaimStore.ReleaseAsync(_db, claim, _logger); // nothing was written at the provider
            throw;
        }

        if (found is not null)
        {
            claim.Complete(found.Id);
            await BillingClaimStore.TrySaveAsync(_db, claim, _logger, CancellationToken.None);
            _logger.LogInformation("Adopted existing Maxio customer {MaxioCustomerId}.", found.Id);
            return CustomerOutcome.Ready(found.Id);
        }

        // 3. Create, with the reference that makes the outcome findable again.
        MaxioCustomerRecord created;
        try
        {
            created = await _gateway.CreateCustomerAsync(userName, email, FirstNameFor(email, userName), LastNameForShopUsers, ct);
        }
        catch (MaxioWriteRejectedException ex)
        {
            if (ex.ProviderStatus == HttpStatus.UnprocessableEntity)
            {
                // A 422 may mean "reference already taken": re-read before treating it as a refusal.
                var settled = await TrySettleAsync(userName);
                if (settled is not null)
                {
                    claim.Complete(settled.Id);
                    await BillingClaimStore.TrySaveAsync(_db, claim, _logger, CancellationToken.None);
                    _logger.LogInformation("CreateCustomer was refused but the customer exists; adopted Maxio customer {MaxioCustomerId}.", settled.Id);
                    return CustomerOutcome.Ready(settled.Id);
                }
            }

            await BillingClaimStore.ReleaseAsync(_db, claim, _logger);
            throw ToCallerFailure(ex);
        }
        catch (MaxioWriteOutcomeUnknownException)
        {
            // The provider may have created the customer. Settle it here by re-reading by reference.
            var settled = await TrySettleAsync(userName);
            if (settled is not null)
            {
                claim.Complete(settled.Id);
                await BillingClaimStore.TrySaveAsync(_db, claim, _logger, CancellationToken.None);
                _logger.LogInformation("Settled an unknown CreateCustomer outcome: Maxio customer {MaxioCustomerId} exists.", settled.Id);
                return CustomerOutcome.Ready(settled.Id);
            }

            claim.MarkUnknown();
            await BillingClaimStore.TrySaveAsync(_db, claim, _logger, CancellationToken.None);
            _logger.LogWarning("CreateCustomer outcome is still unknown; the claim is marked Unknown and the next request settles it.");
            return CustomerOutcome.Pending;
        }

        // 4. Record.
        claim.Complete(created.Id);
        await BillingClaimStore.TrySaveAsync(_db, claim, _logger, CancellationToken.None);
        _logger.LogInformation("Created Maxio customer {MaxioCustomerId}.", created.Id);
        return CustomerOutcome.Ready(created.Id);
    }

    /// <summary>The Maxio customer id for this user without creating anything, or <c>null</c> when none exists.</summary>
    public virtual async Task<int?> FindCustomerIdAsync(string userName, CancellationToken ct)
    {
        var link = await _db.MaxioCustomerLinks.AsNoTracking().SingleOrDefaultAsync(l => l.UserName == userName, ct);
        if (link is { Status: BillingClaimStatus.Completed, MaxioCustomerId: not null })
        {
            return link.MaxioCustomerId;
        }

        var found = await _gateway.FindCustomerByReferenceAsync(userName, ct);
        return found?.Id;
    }

    /// <summary>Re-reads by reference under a fresh budget. Null when not found or when the re-read fails too.</summary>
    private async Task<MaxioCustomerRecord?> TrySettleAsync(string reference)
    {
        using var cts = MaxioCallBudget.ForReconciliation();
        try
        {
            return await _gateway.FindCustomerByReferenceAsync(reference, cts.Token);
        }
        catch (Exception ex) when (ex is MaxioBillingException or OperationCanceledException)
        {
            _logger.LogWarning("Re-reading the Maxio customer by reference failed ({ExceptionType}).", ex.GetType().Name);
            return null;
        }
    }

    internal static MaxioBillingException ToCallerFailure(MaxioWriteRejectedException ex) => ex.ProviderStatus switch
    {
        401 or 403 => new MaxioBillingException(HttpStatus.BadGateway, "The billing provider rejected the shop's credentials.", null, ex),
        429 => new MaxioBillingException(HttpStatus.ServiceUnavailable, "The billing provider is rate limiting requests; try again shortly.", null, ex),
        HttpStatus.UnprocessableEntity => new MaxioBillingException(HttpStatus.UnprocessableEntity, "The billing provider refused the request.", ex.ProviderErrors, ex),
        _ => new MaxioBillingException(HttpStatus.BadGateway, "The billing provider refused the request.", ex.ProviderErrors, ex),
    };

    private static string FirstNameFor(string email, string userName)
    {
        var source = string.IsNullOrWhiteSpace(email) ? userName : email;
        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : source;
        return string.IsNullOrWhiteSpace(local) ? "Shopper" : local;
    }
}
