using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Constants;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class AppIdentityDbContextSeed
{
    public static async Task SeedAsync(AppIdentityDbContext identityDbContext, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {

        if (identityDbContext.Database.IsSqlServer())
        {
            identityDbContext.Database.Migrate();
        }

        if (!await roleManager.RoleExistsAsync(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS))
        {
            await roleManager.CreateAsync(new IdentityRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS));
        }

        var defaultUser = await userManager.FindByNameAsync("demouser@microsoft.com");
        if (defaultUser is null)
        {
            defaultUser = new ApplicationUser
            {
                Id = "demouser",
                UserName = "demouser@microsoft.com",
                Email = "demouser@microsoft.com",
                FirstName = "Demo",
                LastName = "User"
            };
            await userManager.CreateAsync(defaultUser, AuthorizationConstants.DEFAULT_PASSWORD);
        }
        else if (string.IsNullOrWhiteSpace(defaultUser.FirstName) ||
                 string.IsNullOrWhiteSpace(defaultUser.LastName))
        {
            defaultUser.FirstName = "Demo";
            defaultUser.LastName = "User";
            await userManager.UpdateAsync(defaultUser);
        }

        string adminUserName = "admin@microsoft.com";
        var adminUser = await userManager.FindByNameAsync(adminUserName);
        if (adminUser is null)
        {
            adminUser = new ApplicationUser
            {
                Id = "admin",
                UserName = adminUserName,
                Email = adminUserName,
                FirstName = "eShop",
                LastName = "Administrator"
            };
            await userManager.CreateAsync(adminUser, AuthorizationConstants.DEFAULT_PASSWORD);
        }
        else if (string.IsNullOrWhiteSpace(adminUser.FirstName) ||
                 string.IsNullOrWhiteSpace(adminUser.LastName))
        {
            adminUser.FirstName = "eShop";
            adminUser.LastName = "Administrator";
            await userManager.UpdateAsync(adminUser);
        }

        if (adminUser != null)
        {
            if (!await userManager.IsInRoleAsync(adminUser, BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS))
            {
                await userManager.AddToRoleAsync(adminUser, BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
            }
        }
    }
}
