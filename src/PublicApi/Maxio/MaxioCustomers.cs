using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Requests.Customers;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Maps a signed-in shopper to a Maxio customer. The customer's <c>reference</c> is the JWT user name; Maxio keeps
/// references unique per site, which is the claim that stops two requests from creating two customers.
/// </summary>
public class MaxioCustomers
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioCustomers> _logger;

    public MaxioCustomers(MaxioAdvancedBillingClient client, ILogger<MaxioCustomers> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>The Maxio customer id of this user, or null when the user has no customer yet.</summary>
    public async Task<int?> FindCustomerIdAsync(string userName, CancellationToken ct)
    {
        try
        {
            var found = await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = userName }, cancellationToken: ct);
            return found.Customer.Id;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw MaxioErrors.Map(ex, "look up the customer");
        }
    }

    /// <summary>The Maxio customer of this user, created on first use.</summary>
    public async Task<int> EnsureCustomerIdAsync(string userName, CancellationToken ct)
    {
        var existing = await FindCustomerIdAsync(userName, ct);
        if (existing is not null) return existing.Value;

        var (first, last, email) = Describe(userName);
        try
        {
            var created = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = first,
                        LastName = last,
                        Email = email,
                        Reference = userName,
                    }
                }
            }, cancellationToken: ct);
            return created.Customer.Id ?? throw new MaxioProblemException(HttpStatusCode.BadGateway, "Maxio created the customer but answered without its id.");
        }
        catch (Exception ex) when (ex is SdkException)
        {
            // Either a concurrent request created the customer first (Maxio refuses a duplicate reference) or the
            // connection failed after Maxio acted: in both cases the customer may exist, so read it back by reference.
            var settled = await FindCustomerIdAfterFailureAsync(userName, ct);
            if (settled is not null)
            {
                _logger.LogInformation("Customer for {User} already existed after a failed create ({Failure})", userName, ex.GetType().Name);
                return settled.Value;
            }
            throw MaxioErrors.Map(ex, "create the customer", writeMayHaveLanded: true);
        }
    }

    private async Task<int?> FindCustomerIdAfterFailureAsync(string userName, CancellationToken ct)
    {
        try
        {
            return await FindCustomerIdAsync(userName, ct);
        }
        catch (MaxioProblemException)
        {
            return null;
        }
    }

    private static (string First, string Last, string Email) Describe(string userName)
    {
        var at = userName.IndexOf('@');
        var local = at > 0 ? userName[..at] : userName;
        var email = at > 0 && at < userName.Length - 1 ? userName : $"{Uri.EscapeDataString(userName).Replace("%", "")}@shopper.invalid";
        return (local, "Shopper", email);
    }
}
