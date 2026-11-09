using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class PaymentsStartup
{
    public static void AddPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PaymentSettings>(configuration.GetSection("Payments"));
        services.AddHttpClient();
        services.AddSingleton<StaxGateway>();

        if (bool.TryParse(configuration["UseOnlyInMemoryDatabase"], out var inMemory) && inMemory)
        {
            services.AddDbContext<PaymentsContext>(o => o.UseInMemoryDatabase("Payments"));
        }
        else
        {
            services.AddDbContext<PaymentsContext>(o => o.UseSqlServer(configuration.GetConnectionString("CatalogConnection")));
        }

        services.AddScoped<ShopperService>();
        services.AddScoped<OrderService>();
        services.AddScoped<InvoiceAndSellerService>();
        services.AddHostedService<PaymentReconciler>();
    }

    /// <summary>Creates the payment tables (next to the catalog tables on a relational database).</summary>
    public static async Task InitializePaymentsAsync(this PaymentsContext db)
    {
        if (db.Database.IsRelational())
        {
            await db.Database.EnsureCreatedAsync();
            var creator = db.GetService<IRelationalDatabaseCreator>();
            try
            {
                // the database already exists, so create just our tables
                await creator.CreateTablesAsync();
            }
            catch (Exception)
            {
                // tables are already there
            }
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }
    }

    /// <summary>The caller's e-mail: the JWT name claim, which is the shop user name.</summary>
    public static string Caller(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name
                   ?? user.FindFirstValue(ClaimTypes.Email)
                   ?? user.FindFirstValue("email")
                   ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(name)
            ? throw new PaymentException(401, "The token does not identify a shopper.")
            : ShopperService.Normalize(name);
    }
}
