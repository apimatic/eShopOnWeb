using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public interface ISubscriptionService
{
    Task<ApplicationCore.Entities.MaxioCustomer?> GetOrCreateMaxioCustomerAsync(ApplicationUser user);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioApiClient _maxioClient;
    private readonly IRepository<ApplicationCore.Entities.MaxioCustomer> _maxioCustomerRepository;

    public SubscriptionService(
        IMaxioApiClient maxioClient,
        IRepository<ApplicationCore.Entities.MaxioCustomer> maxioCustomerRepository)
    {
        _maxioClient = maxioClient;
        _maxioCustomerRepository = maxioCustomerRepository;
    }

    public async Task<ApplicationCore.Entities.MaxioCustomer?> GetOrCreateMaxioCustomerAsync(ApplicationUser user)
    {
        // Check if we already have a mapping
        var existing = (await _maxioCustomerRepository.ListAsync()).FirstOrDefault(m => m.ApplicationUserId == user.Id);
        if (existing != null)
        {
            return existing;
        }

        // Create or get Maxio customer
        var maxioCustomer = await _maxioClient.GetOrCreateCustomerAsync(
            user.Email ?? "",
            user.UserName ?? "",
            user.UserName ?? "",
            user.Id);

        if (maxioCustomer == null)
        {
            return null;
        }

        // Store the mapping
        var mapping = new ApplicationCore.Entities.MaxioCustomer
        {
            ApplicationUserId = user.Id,
            MaxioId = maxioCustomer.MaxioId,
            MaxioReference = maxioCustomer.Reference,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _maxioCustomerRepository.AddAsync(mapping);
        await _maxioCustomerRepository.SaveChangesAsync();

        return mapping;
    }
}
