using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContextSeed
{
    public static async Task SeedAsync(AppIdentityDbContext identityDbContext, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {

        if (identityDbContext.Database.IsSqlServer())
        {
            identityDbContext.Database.Migrate();
        }

        await roleManager.CreateAsync(new IdentityRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS));

        var defaultUser = new ApplicationUser { UserName = "demouser@microsoft.com", Email = "demouser@microsoft.com" };
        await userManager.CreateAsync(defaultUser, AuthorizationConstants.DEFAULT_PASSWORD);

        string adminUserName = "admin@microsoft.com";
        var adminUser = new ApplicationUser { UserName = adminUserName, Email = adminUserName };
        await userManager.CreateAsync(adminUser, AuthorizationConstants.DEFAULT_PASSWORD);
        adminUser = await userManager.FindByNameAsync(adminUserName);
        if (adminUser != null)
        {
            await userManager.AddToRoleAsync(adminUser, BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        }

        await SeedSubscriptionPlansAsync(identityDbContext);
    }

    private static async Task SeedSubscriptionPlansAsync(AppIdentityDbContext context)
    {
        if (await context.SubscriptionPlans.AnyAsync())
            return;

        var plans = new[]
        {
            new SubscriptionPlan
            {
                Handle = "basic-plan",
                Name = "Basic Plan",
                Description = "Our basic subscription plan with essential features",
                PriceInCents = 2900,
                Interval = 1,
                IntervalUnit = "month",
                MaxioProductId = 7126958
            },
            new SubscriptionPlan
            {
                Handle = "eshop-pro",
                Name = "Pro Plan",
                Description = "Our professional plan with advanced features",
                PriceInCents = 29900,
                Interval = 1,
                IntervalUnit = "month",
                MaxioProductId = 7126957
            }
        };

        foreach (var plan in plans)
        {
            context.SubscriptionPlans.Add(plan);
        }

        await context.SaveChangesAsync();
    }
}
