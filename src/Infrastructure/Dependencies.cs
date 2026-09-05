using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class Dependencies
{
    public static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        bool useOnlyInMemoryDatabase = false;
        if (configuration["UseOnlyInMemoryDatabase"] != null)
        {
            useOnlyInMemoryDatabase = bool.Parse(configuration["UseOnlyInMemoryDatabase"]!);
        }

        if (useOnlyInMemoryDatabase)
        {
            services.AddDbContext<CatalogContext>(c =>
               c.UseInMemoryDatabase("Catalog"));

            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseInMemoryDatabase("Identity"));
        }
        else
        {
            // use real database
            // Requires LocalDB which can be installed with SQL Server Express 2016
            // https://www.microsoft.com/en-us/download/details.aspx?id=54284
            services.AddDbContext<CatalogContext>(c =>
                c.UseSqlServer(configuration.GetConnectionString("CatalogConnection")));

            // Add Identity DbContext
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("IdentityConnection")));
        }

        ConfigureMaxio(configuration, services);
    }

    private static void ConfigureMaxio(IConfiguration configuration, IServiceCollection services)
    {
        var maxioOptions = configuration.GetSection(MaxioOptions.ConfigSectionName).Get<MaxioOptions>() ?? new MaxioOptions();
        services.AddSingleton(maxioOptions);

        services.AddHttpClient<IMaxioClient, MaxioClient>(client =>
        {
            var baseUrl = string.IsNullOrWhiteSpace(maxioOptions.BaseUrl)
                ? $"https://{maxioOptions.Subdomain}.chargify.com/"
                : maxioOptions.BaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);

            var basicAuthValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{maxioOptions.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuthValue);
        });

        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();
    }
}
