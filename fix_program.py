with open('src/PublicApi/Program.cs','r') as f: content = f.read()
old = "// Maxio SDK temporarily disabled for isolation test\nbuilder.Services.AddScoped<IMaxioBillingService, MaxioBillingService>;"
new = """builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var maxioConfig = cfg.GetSection("Maxio");
    var apiKey = maxioConfig["ApiKey"] ?? Environment.GetEnvironmentVariable("MAXIO_API_KEY") ?? throw new InvalidOperationException("Maxio:ApiKey missing");
    var subdomain = maxioConfig["Subdomain"] ?? Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN") ?? "cp-exp-1";
    var envName = maxioConfig["Environment"] ?? Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT") ?? "US";
    var options = new MaxioAdvancedBillingClientOptions
    {
        BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
        {
            Username = apiKey,
            Password = "x"
        }
    };
    if (envName.Equals("EU", StringComparison.OrdinalIgnoreCase))
        options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Eu;
    else
        options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us;
    var baseUrl = maxioConfig["BaseUrl"];
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        options.Server.Production.Us.BaseUrl = baseUrl;
        options.Server.Production.Eu.BaseUrl = baseUrl;
    }
    else
    {
        string derived = envName.Equals("EU", StringComparison.OrdinalIgnoreCase)
            ? $"https://{subdomain}.ebilling.maxio.com"
            : $"https://{subdomain}.chargify.com";
        options.Server.Production.Us.BaseUrl = derived;
        options.Server.Production.Eu.BaseUrl = derived;
    }
    var httpClient = new System.Net.Http.HttpClient();
    return new MaxioAdvancedBillingClient(httpClient, options);
});

builder.Services.AddScoped<IMaxioBillingService, MaxioBillingService>;"""
content = content.replace(old, new)
with open('src/PublicApi/Program.cs','w') as f: f.write(content)
print("done")
