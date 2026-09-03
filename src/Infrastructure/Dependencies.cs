using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Servers;

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
    }

    public static void ConfigureTwilioServices(IConfiguration configuration, IServiceCollection services)
    {
        services.AddOptions<TwilioSettings>()
            .Bind(configuration.GetRequiredSection(TwilioSettings.SectionName))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.AccountSid), "Twilio:AccountSid is not configured.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.AuthToken), "Twilio:AuthToken is not configured.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.FromNumber), "Twilio:FromNumber is not configured.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.MessagingServiceSid), "Twilio:MessagingServiceSid is not configured.")
            .Validate(settings => string.IsNullOrWhiteSpace(settings.BaseUrl) ||
                                  Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _),
                "Twilio:BaseUrl must be an absolute URL when configured.")
            .ValidateOnStart();

        const string twilioHttpClientName = "TwilioMessaging";
        services.AddHttpClient(twilioHttpClientName, client => client.Timeout = TimeSpan.FromSeconds(6))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<TwilioSettings>>().Value;
            var options = new TwilioSdkClientOptions
            {
                Environment = ServerEnvironment.Production,
                AccountSidAuthToken = new BasicAuthCredentials
                {
                    Username = settings.AccountSid,
                    Password = settings.AuthToken
                },
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 2,
                    Timeout = TimeSpan.FromSeconds(5)
                },
                Logging = new LoggingOptions
                {
                    LoggerFactory = NullLoggerFactory.Instance,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                    LogRequestBody = false
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(twilioHttpClientName);
            return new TwilioSdkClient(httpClient, options);
        });

        services.AddSingleton<ITwilioMessagingGateway, TwilioMessagingGateway>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<OrderNotificationService>();
        services.AddHostedService<ScheduledNotificationCancellationWorker>();
    }
}
