using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure;

public static class Dependencies
{
    public static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        ConfigureTwilio(configuration, services);

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
    }

    private static void ConfigureTwilio(IConfiguration configuration, IServiceCollection services)
    {
        services.Configure<TwilioSettings>(configuration.GetSection(TwilioSettings.SectionName));

        static void ConfigureAuth(HttpClient client, TwilioSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.AccountSid) && !string.IsNullOrWhiteSpace(settings.AuthToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}")));
            }
        }

        // Messaging API client. Twilio:BaseUrl, when set, is used verbatim as the base
        // address for every messaging-API call instead of the provider default.
        services.AddHttpClient(TwilioSmsService.MessagingHttpClientName, (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
                ? TwilioSmsService.DefaultMessagingBaseUrl
                : settings.BaseUrl!;
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            ConfigureAuth(client, settings);
        });

        // Lookup API client (separate host; not governed by Twilio:BaseUrl).
        services.AddHttpClient(TwilioSmsService.LookupHttpClientName, (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<TwilioSettings>>().Value;
            client.BaseAddress = new Uri(TwilioSmsService.LookupBaseUrl + "/");
            ConfigureAuth(client, settings);
        });

        services.AddScoped<ISmsService, TwilioSmsService>();
    }
}
