using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
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
        services.Configure<TwilioOptions>(configuration.GetSection(TwilioOptions.SectionName));

        static AuthenticationHeaderValue BasicAuth(IServiceProvider sp)
        {
            var options = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
            return new AuthenticationHeaderValue("Basic", credentials);
        }

        // Messaging API: honors the optional Twilio:BaseUrl override, used verbatim.
        services.AddHttpClient<IMessagingClient, TwilioMessagingClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
            client.BaseAddress = new Uri(options.MessagingBaseUrl);
            client.DefaultRequestHeaders.Authorization = BasicAuth(sp);
        });

        // Lookups API: always served from lookups.twilio.com per its spec; Twilio:BaseUrl does not govern it.
        services.AddHttpClient<IPhoneNumberValidator, TwilioPhoneNumberValidator>((sp, client) =>
        {
            client.BaseAddress = new Uri(TwilioOptions.LookupsBaseUrl);
            client.DefaultRequestHeaders.Authorization = BasicAuth(sp);
        });

        services.AddScoped<IOrderNotificationService, OrderNotificationService>();
    }
}
