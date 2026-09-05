using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.CONFIG_SECTION));
        services.AddHttpClient<IMaxioSubscriptionService, MaxioSubscriptionService>((serviceProvider, client) =>
        {
            var maxioOptions = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;

            var baseUrl = string.IsNullOrWhiteSpace(maxioOptions.BaseUrl)
                ? $"https://{maxioOptions.Subdomain}.chargify.com"
                : maxioOptions.BaseUrl;
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

            var basicAuthValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{maxioOptions.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuthValue);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });
    }
}
