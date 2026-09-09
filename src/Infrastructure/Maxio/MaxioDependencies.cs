using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioDependencies
{
    public static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddSingleton<IValidateOptions<MaxioOptions>, ValidateMaxioOptions>();
        services.AddMemoryCache();
        services.AddHttpClient<IMaxioGateway, MaxioGateway>((sp, client) =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });
        services.AddScoped<ISubscriptionManager, SubscriptionManager>();
    }
}

public sealed class ValidateMaxioOptions : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        try
        {
            options.Validate();
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException ex)
        {
            return ValidateOptionsResult.Fail(ex.Message);
        }
    }
}
