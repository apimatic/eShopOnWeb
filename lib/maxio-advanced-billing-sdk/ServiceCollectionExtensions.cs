using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MaxioAdvancedBilling;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure = null)
        {
            var options = new MaxioAdvancedBillingClientOptions();
            configure?.Invoke(options);
            services.AddHttpClient();
            services.AddSingleton(sp =>
                {
                    options.Logging =
                        options.Logging with
                        {
                            LoggerFactory = options.Logging.LoggerFactory ?? sp.GetService<ILoggerFactory>()
                        };
                    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                    var httpClient = httpClientFactory.CreateClient();
                    return new MaxioAdvancedBillingClient(httpClient, options);
                });
            return services;
        }
    }
}
