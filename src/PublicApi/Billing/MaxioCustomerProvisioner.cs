using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Requests.Customers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>Optional names for a new billing customer; eShopOnWeb accounts carry only an email.</summary>
public record CustomerName(string? FirstName, string? LastName);

/// <summary>
/// Ensures exactly one Maxio customer exists per eShopOnWeb buyer: claim (primary key on buyer id) → look up by
/// the deterministic customer reference → create → record the Maxio customer id.
/// </summary>
public class MaxioCustomerProvisioner
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IBillingClaimStore _claims;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioCallBudget _budget;
    private readonly TimeProvider _clock;
    private readonly ILogger<MaxioCustomerProvisioner> _logger;

    public MaxioCustomerProvisioner(
        MaxioAdvancedBillingClient client,
        IBillingClaimStore claims,
        UserManager<ApplicationUser> userManager,
        MaxioCallBudget budget,
        TimeProvider clock,
        ILogger<MaxioCustomerProvisioner> logger)
    {
        _client = client;
        _claims = claims;
        _userManager = userManager;
        _budget = budget;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>The reference Maxio stores on the customer; Maxio allows one customer per reference.</summary>
    public static string CustomerReferenceFor(string buyerId) => $"eshop:{buyerId}";

    /// <summary>
    /// Returns the Maxio customer id for the buyer, or <c>null</c> when a create was sent but its outcome could not
    /// be confirmed yet (the claim stays unsettled and a later request settles it).
    /// </summary>
    public async Task<int?> EnsureCustomerAsync(string buyerId, CustomerName? name, CancellationToken cancellationToken)
    {
        var existing = await _claims.GetCustomerAsync(buyerId, cancellationToken);
        if (existing is { IsSettled: true, ProviderCustomerId: int linkedId })
        {
            return linkedId;
        }

        var claim = new BillingCustomer(buyerId, CustomerReferenceFor(buyerId), _clock.GetUtcNow());
        if (existing is not null || !await _claims.TryClaimAsync(claim, cancellationToken))
        {
            // Another request holds (or held) the claim for this buyer.
            var held = existing
                ?? await _claims.GetCustomerAsync(buyerId, cancellationToken)
                ?? throw new BillingException(StatusCodes.Status409Conflict, "Your billing account is being set up. Please retry shortly.");

            if (held is { IsSettled: true, ProviderCustomerId: int heldId })
            {
                return heldId;
            }

            // Settle it if the earlier attempt actually reached Maxio.
            var settledId = await ReadCustomerIdByReferenceAsync(held.CustomerReference, cancellationToken);
            if (settledId is int foundId)
            {
                await LinkAsync(held, foundId, cancellationToken);
                return foundId;
            }

            if (!held.IsStale(_clock.GetUtcNow(), _budget.StaleClaimAfter))
            {
                _logger.LogInformation("Billing customer claim {CustomerReference} is held by an in-flight request.", held.CustomerReference);
                throw new BillingException(StatusCodes.Status409Conflict, "Your billing account is being set up. Please retry shortly.");
            }

            held.TakeOver(_clock.GetUtcNow());
            if (!await _claims.TryUpdateAsync(held, cancellationToken))
            {
                throw new BillingException(StatusCodes.Status409Conflict, "Your billing account is being set up. Please retry shortly.");
            }

            _logger.LogWarning("Took over stale billing customer claim {CustomerReference}.", held.CustomerReference);
            return await CreateAsync(held, name, cancellationToken);
        }

        // Claim won. The customer may already exist in Maxio (e.g. the local store was reset) — never create twice.
        var existingId = await ReadCustomerIdByReferenceAsync(claim.CustomerReference, cancellationToken);
        if (existingId is int id)
        {
            await LinkAsync(claim, id, cancellationToken);
            return id;
        }

        return await CreateAsync(claim, name, cancellationToken);
    }

    /// <summary>Reads the Maxio customer id for the buyer without creating anything; null when there is none.</summary>
    public async Task<int?> FindCustomerIdAsync(string buyerId, CancellationToken cancellationToken)
    {
        var link = await _claims.GetCustomerAsync(buyerId, cancellationToken);
        if (link is { IsSettled: true, ProviderCustomerId: int linkedId })
        {
            return linkedId;
        }

        var reference = link?.CustomerReference ?? CustomerReferenceFor(buyerId);
        var foundId = await ReadCustomerIdByReferenceAsync(reference, cancellationToken);
        if (foundId is int id && link is not null)
        {
            // An unsettled claim whose create did land: settle it.
            await LinkAsync(link, id, cancellationToken);
        }

        return foundId;
    }

    private async Task<int?> CreateAsync(BillingCustomer claim, CustomerName? name, CancellationToken cancellationToken)
    {
        const string operation = "CreateCustomer";
        var body = await BuildCustomerAsync(claim, name, cancellationToken);

        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new MaxioAdvancedBilling.Models.CreateCustomerRequest { Customer = body }
                },
                cancellationToken: cancellationToken);

            if (response.Customer?.Id is int createdId)
            {
                await LinkAsync(claim, createdId, cancellationToken);
                _logger.LogInformation("Created Maxio customer {MaxioCustomerId} for {CustomerReference}.", createdId, claim.CustomerReference);
                return createdId;
            }

            _logger.LogWarning("Maxio {Operation} succeeded without a customer id for {CustomerReference}; settling by reference.", operation, claim.CustomerReference);
            return await SettleUnknownAsync(claim);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            var status = ex.StatusCode;
            var errors = ReadErrors(ex.Error);

            // A customer already carrying this reference is refused by Maxio; that customer is ours.
            if (status == HttpStatusCode.UnprocessableEntity)
            {
                var existingId = await TryReadCustomerIdForSettlementAsync(claim.CustomerReference);
                if (existingId is int id)
                {
                    await LinkAsync(claim, id, CancellationToken.None);
                    return id;
                }
            }

            if ((int)status >= 500)
            {
                return await SettleUnknownAsync(claim);
            }

            await ReleaseAsync(claim);
            throw MaxioFailures.FromStatus(_logger, operation, status, errors, ex);
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 200 and < 300)
        {
            _logger.LogWarning("Maxio {Operation} returned an unreadable {StatusCode} body for {CustomerReference}; settling by reference.", operation, (int)ex.StatusCode, claim.CustomerReference);
            return await SettleUnknownAsync(claim);
        }
        catch (SdkConnectionException ex)
        {
            // Includes SdkTimeoutException: the create may have landed.
            _logger.LogWarning("Maxio {Operation} lost its connection for {CustomerReference} ({ExceptionType}); settling by reference.", operation, claim.CustomerReference, ex.GetType().Name);
            return await SettleUnknownAsync(claim);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Maxio {Operation} was cancelled in flight for {CustomerReference}; settling by reference.", operation, claim.CustomerReference);
            return await SettleUnknownAsync(claim);
        }
        catch (SdkException ex)
        {
            // Refused before or without acting (unreadable error body, credentials not applied).
            await ReleaseAsync(claim);
            throw MaxioFailures.FromSdk(_logger, operation, ex);
        }
    }

    /// <summary>
    /// The create's outcome is unknown: re-read by the reference it carried. Found → linked. Not found or unreadable
    /// → the claim is marked Unknown and the next request for this buyer settles it before anything else.
    /// </summary>
    private async Task<int?> SettleUnknownAsync(BillingCustomer claim)
    {
        var foundId = await TryReadCustomerIdForSettlementAsync(claim.CustomerReference);
        if (foundId is int id)
        {
            await LinkAsync(claim, id, CancellationToken.None);
            _logger.LogInformation("Settled unknown Maxio customer create {CustomerReference}: customer {MaxioCustomerId} exists.", claim.CustomerReference, id);
            return id;
        }

        claim.MarkUnknown(_clock.GetUtcNow());
        await _claims.TryUpdateAsync(claim, CancellationToken.None);
        _logger.LogWarning("Maxio customer create {CustomerReference} has an unknown outcome; left for the next request to settle.", claim.CustomerReference);
        return null;
    }

    private async Task<int?> TryReadCustomerIdForSettlementAsync(string reference)
    {
        using var settlement = _budget.StartSettlement();
        try
        {
            return await ReadCustomerIdByReferenceAsync(reference, settlement.Token);
        }
        catch (BillingException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Looks the customer up by reference: the id, or <c>null</c> when Maxio has no such customer.</summary>
    private async Task<int?> ReadCustomerIdByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        const string operation = "ReadCustomerByReference";
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: cancellationToken);

            return response.Customer?.Id
                ?? throw new BillingException(StatusCodes.Status502BadGateway, "The billing provider returned a response that could not be processed.");
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw MaxioFailures.FromSdk(_logger, operation, ex);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.CreateCustomer> BuildCustomerAsync(BillingCustomer claim, CustomerName? name, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(claim.BuyerId);
        var email = user?.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            await ReleaseAsync(claim);
            throw new BillingException(StatusCodes.Status422UnprocessableEntity, "Your account has no email address, which billing requires.");
        }

        // eShopOnWeb accounts have no name fields; Maxio requires first and last name.
        var localPart = email.Split('@')[0];
        return new MaxioAdvancedBilling.Models.CreateCustomer
        {
            FirstName = string.IsNullOrWhiteSpace(name?.FirstName) ? localPart : name.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(name?.LastName) ? "Customer" : name.LastName.Trim(),
            Email = email,
            Reference = claim.CustomerReference
        };
    }

    private static IReadOnlyList<string> ReadErrors(CreateCustomerError error)
    {
        if (error.TryGetCustomerErrorResponse1(out var body))
        {
            if (body.Errors is not null)
            {
                if (body.Errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError.Customer))
                {
                    return new[] { customerError.Customer };
                }

                if (body.Errors.TryGetListOfString(out var messages))
                {
                    return messages.Where(m => !string.IsNullOrWhiteSpace(m)).ToArray();
                }
            }

            return Array.Empty<string>();
        }

        // TryGetRawError (statuses without a typed body) carries no caller-safe messages.
        return Array.Empty<string>();
    }

    private async Task LinkAsync(BillingCustomer claim, int providerCustomerId, CancellationToken cancellationToken)
    {
        claim.MarkLinked(providerCustomerId, _clock.GetUtcNow());
        if (!await _claims.TryUpdateAsync(claim, cancellationToken))
        {
            _logger.LogWarning("Billing customer claim {CustomerReference} changed while being linked; the other holder will record it.", claim.CustomerReference);
        }
    }

    private async Task ReleaseAsync(BillingCustomer claim)
    {
        await _claims.ReleaseAsync(claim, CancellationToken.None);
    }
}
