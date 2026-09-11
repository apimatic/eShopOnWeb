using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.PublicApi;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioCustomerService
{
    Task<int> EnsureCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken ct = default);
    Task<int?> FindCustomerIdAsync(string reference, CancellationToken ct = default);
}

public class MaxioCustomerService : IMaxioCustomerService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly IMaxioCustomerMapping _mapping;
    private readonly MaxioConfiguration _config;

    public MaxioCustomerService(
        MaxioAdvancedBillingClient client,
        IMaxioCustomerMapping mapping,
        IOptions<MaxioConfiguration> config)
    {
        _client = client;
        _mapping = mapping;
        _config = config.Value;
    }

    public async Task<int> EnsureCustomerAsync(string userId, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        // Idempotent: check mapping first (survives within run)
        var mapped = _mapping.GetCustomerId(userId);
        if (mapped.HasValue)
        {
            // Verify still exists; if not, fall through to recreate
            try
            {
                var read = await _client.Customers.ReadCustomerByReference(userId, ct: ct);
                if (read.Customer != null)
                {
                    _mapping.SetCustomerId(userId, read.Customer.Id ?? mapped.Value);
                    return read.Customer.Id ?? mapped.Value;
                }
            }
            catch
            {
                // fall through
            }
        }

        // Search by email / reference
        int? existingId = null;
        try
        {
            var list = await _client.Customers.ListCustomers(direction: null, dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, q: email, perPage: 5, ct: ct);
            foreach (var resp in list)
            {
                var c = resp.Customer;
                if (c != null && (c.Email?.Equals(email, StringComparison.OrdinalIgnoreCase) == true || c.Reference == userId))
                {
                    existingId = c.Id;
                    break;
                }
            }
        }
        catch { /* ignore search errors */ }

        if (!existingId.HasValue)
        {
            try
            {
                var create = new CreateCustomerRequest
                {
                    Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                    {
                        Email = email,
                        Reference = userId,
                        FirstName = firstName,
                        LastName = lastName
                    }
                };
                var created = await _client.Customers.CreateCustomer(create, ct: ct);
                if (created.Customer != null && created.Customer.Id.HasValue)
                {
                    existingId = created.Customer.Id.Value;
                }
            }
            catch (Exception ex)
            {
                // Defensive: if duplicate reference causes error, retry lookup by reference
                try
                {
                    var retry = await _client.Customers.ReadCustomerByReference(userId, ct: ct);
                    if (retry.Customer != null && retry.Customer.Id.HasValue)
                    {
                        existingId = retry.Customer.Id.Value;
                    }
                }
                catch
                {
                    throw new InvalidOperationException("Failed to create or retrieve Maxio customer: " + ex.Message, ex);
                }
            }
        }

        if (!existingId.HasValue)
            throw new InvalidOperationException("Could not determine Maxio customer id for user: " + userId);

        _mapping.SetCustomerId(userId, existingId.Value);
        return existingId.Value;
    }

    public async Task<int?> FindCustomerIdAsync(string reference, CancellationToken ct = default)
    {
        var mapped = _mapping.GetCustomerId(reference);
        if (mapped.HasValue) return mapped.Value;

        try
        {
            var read = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            if (read.Customer != null && read.Customer.Id.HasValue)
            {
                _mapping.SetCustomerId(reference, read.Customer.Id.Value);
                return read.Customer.Id.Value;
            }
        }
        catch { }

        return null;
    }
}
