using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class SubscriptionContextSeed
{
    public static async Task SeedAsync(CatalogContext catalogContext, IMaxioService maxioService, MaxioSettings settings, ILogger logger, int retry = 0)
    {
        var retryForAvailability = retry;
        try
        {
            if (string.IsNullOrEmpty(settings.ProductFamilyHandle) || string.IsNullOrEmpty(settings.ApiKey))
            {
                logger.LogInformation("Maxio configuration not set, skipping subscription plan seeding");
                return;
            }

            if (!await catalogContext.SubscriptionPlans.AnyAsync())
            {
                try
                {
                    var products = await maxioService.ListProductsByFamilyHandleAsync(settings.ProductFamilyHandle);

                    if (products.Count > 0)
                    {
                        var plans = products.Select(p => new SubscriptionPlan
                        {
                            MaxioProductId = p.Id,
                            Handle = p.Handle,
                            Name = p.Name,
                            Description = p.Description,
                            PriceInCents = p.PriceInCents,
                            IntervalValue = p.Interval,
                            IntervalUnit = p.IntervalUnit,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        }).ToList();

                        await catalogContext.SubscriptionPlans.AddRangeAsync(plans);
                        await catalogContext.SaveChangesAsync();
                        logger.LogInformation("Seeded {Count} subscription plans from Maxio", plans.Count);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to seed subscription plans from Maxio, continuing with empty plans");
                }
            }
        }
        catch (Exception ex)
        {
            if (retryForAvailability >= 3) throw;

            retryForAvailability++;
            logger.LogError(ex, "Error seeding subscription plans");
            await SeedAsync(catalogContext, maxioService, settings, logger, retryForAvailability);
            throw;
        }
    }
}
