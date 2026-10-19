using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSettingsStartupCheck : IHostedService
{
    public MaxioSettingsStartupCheck(IOptions<MaxioSettings> options)
    {
        var settings = options.Value;
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            missing.Add("Maxio:ApiKey");
        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
            missing.Add("Maxio:ProductFamilyHandle");
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Subdomain))
            missing.Add("Maxio:Subdomain");

        if (missing.Count > 0)
            throw new InvalidOperationException(string.Join(", ", missing) + " is not configured.");
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
