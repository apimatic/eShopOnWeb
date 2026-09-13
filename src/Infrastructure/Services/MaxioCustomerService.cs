using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Models;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioCustomerService : IMaxioCustomerService
{
    private readonly MaxioAdvancedBillingClient _client;

    public MaxioCustomerService(MaxioAdvancedBillingClient client)
    {
        _client = client;
    }

    public async Task<MaxioCustomerResult> EnsureCustomerExistsAsync(string email, string firstName, string lastName, CancellationToken ct)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(reference: email, ct: ct);
            var c = existing.Customer!;
            return new MaxioCustomerResult
            {
                Id = (int)(c.Id ?? 0),
                Email = c.Email,
                FirstName = c.FirstName,
                LastName = c.LastName
            };
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = email
                }
            };
            var created = await _client.Customers.CreateCustomer(body: createRequest, ct: ct);
            var c = created.Customer!;
            return new MaxioCustomerResult
            {
                Id = (int)(c.Id ?? 0),
                Email = c.Email,
                FirstName = c.FirstName,
                LastName = c.LastName
            };
        }
    }
}
