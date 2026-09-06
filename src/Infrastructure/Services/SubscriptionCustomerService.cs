using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class SubscriptionCustomerService : ISubscriptionCustomerService
{
    private readonly CatalogContext _context;

    public SubscriptionCustomerService(CatalogContext context)
    {
        _context = context;
    }

    public async Task<SubscriptionCustomer?> GetByUserIdAsync(string userId)
    {
        return await _context.SubscriptionCustomers
            .FirstOrDefaultAsync(x => x.UserId == userId);
    }

    public async Task<SubscriptionCustomer?> GetByMaxioCustomerIdAsync(int maxioCustomerId)
    {
        return await _context.SubscriptionCustomers
            .FirstOrDefaultAsync(x => x.MaxioCustomerId == maxioCustomerId);
    }

    public async Task<SubscriptionCustomer> AddAsync(string userId, int maxioCustomerId)
    {
        var subscriptionCustomer = new SubscriptionCustomer
        {
            UserId = userId,
            MaxioCustomerId = maxioCustomerId,
        };
        _context.SubscriptionCustomers.Add(subscriptionCustomer);
        await _context.SaveChangesAsync();
        return subscriptionCustomer;
    }
}
