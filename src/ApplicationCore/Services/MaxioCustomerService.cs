using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class MaxioCustomerService
{
    private readonly IRepository<MaxioCustomerMapping> _mappingRepository;

    public MaxioCustomerService(IRepository<MaxioCustomerMapping> mappingRepository)
    {
        _mappingRepository = mappingRepository;
    }

    public async Task<int?> GetMaxioCustomerIdAsync(string userId)
    {
        var mapping = (await _mappingRepository.ListAsync(
            new MaxioCustomerMappingByUserIdSpec(userId))).FirstOrDefault();

        return mapping?.MaxioCustomerId;
    }

    public async Task SaveMaxioCustomerMappingAsync(string userId, int maxioCustomerId)
    {
        var existing = (await _mappingRepository.ListAsync(
            new MaxioCustomerMappingByUserIdSpec(userId))).FirstOrDefault();

        if (existing != null)
        {
            existing.MaxioCustomerId = maxioCustomerId;
            await _mappingRepository.UpdateAsync(existing);
        }
        else
        {
            var mapping = new MaxioCustomerMapping
            {
                UserId = userId,
                MaxioCustomerId = maxioCustomerId,
                CreatedAt = DateTime.UtcNow
            };
            await _mappingRepository.AddAsync(mapping);
        }
    }
}
