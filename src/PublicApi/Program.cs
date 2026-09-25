using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using BlazorShared;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Servers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Logging;
using Microsoft.eShopWeb.PublicApi;
using Microsoft.eShopWeb.PublicApi.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MinimalApi.Endpoint.Configurations.Extensions;
using MinimalApi.Endpoint.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpoints();

// Use to force loading of appsettings.json of test project
builder.Configuration.AddConfigurationFile("appsettings.test.json");
builder.Logging.AddConsole();

Microsoft.eShopWeb.Infrastructure.Dependencies.ConfigureServices(builder.Configuration, builder.Services);

builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
        .AddEntityFrameworkStores<AppIdentityDbContext>()
        .AddDefaultTokenProviders();

builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfRepository<>));
builder.Services.Configure<CatalogSettings>(builder.Configuration);
var catalogSettings = builder.Configuration.Get<CatalogSettings>() ?? new CatalogSettings();
builder.Services.AddSingleton<IUriComposer>(new UriComposer(catalogSettings));
builder.Services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
builder.Services.AddScoped<ITokenClaimsService, IdentityTokenClaimService>();

// ---- PayPal payments integration ----
// Map the PAYPAL_* environment variables onto the PayPal: configuration section. No value is
// hard-coded anywhere in the repo; a different PayPal account is used by supplying different
// environment variables / user-secrets for these keys.
var payPalConfig = new Dictionary<string, string?>();
void MapPayPalSetting(string environmentVariable, string key)
{
    var value = Environment.GetEnvironmentVariable(environmentVariable);
    if (!string.IsNullOrWhiteSpace(value))
    {
        payPalConfig[$"{PayPalOptions.SectionName}:{key}"] = value;
    }
}
MapPayPalSetting("PAYPAL_CLIENT_ID", nameof(PayPalOptions.ClientId));
MapPayPalSetting("PAYPAL_CLIENT_SECRET", nameof(PayPalOptions.ClientSecret));
MapPayPalSetting("PAYPAL_ENVIRONMENT", nameof(PayPalOptions.Environment));
MapPayPalSetting("PAYPAL_CURRENCY", nameof(PayPalOptions.Currency));
MapPayPalSetting("PAYPAL_BASE_URL", nameof(PayPalOptions.BaseUrl));
builder.Configuration.AddInMemoryCollection(payPalConfig);

// Fail fast at startup when a credential/currency/environment is missing or unsupported, rather than
// discovering it as a 401 on the first call in production. Both credential parts are checked.
builder.Services.AddOptions<PayPalOptions>()
    .Bind(builder.Configuration.GetSection(PayPalOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => o.ResolveEnvironment() is not null,
        "PayPal:Environment must be a supported PayPal environment (e.g. 'sandbox').")
    .ValidateOnStart();

const string PayPalHttpClientName = "PayPal";
builder.Services.AddHttpClient(PayPalHttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    });

// Single, long-lived SDK client (its options — including the secret — are captured once at
// registration, so a rotated secret takes effect on process restart).
builder.Services.AddSingleton<PayPalServerSdkClient>(sp =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(PayPalHttpClientName);
    var payPalOptions = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    var clientOptions = new PayPalServerSdkClientOptions
    {
        Environment = payPalOptions.ResolveEnvironment() ?? ServerEnvironment.Sandbox,
        Oauth2 = new OAuth2ClientCredentials
        {
            ClientId = payPalOptions.ClientId,
            ClientSecret = payPalOptions.ClientSecret,
        },
        // LoggerFactory is set explicitly and request-body logging stays OFF, so card data is never
        // logged and the PAYPALSERVERSDKCLIENT_LOG env var cannot switch body logging on externally.
        Logging = new LoggingOptions
        {
            LoggerFactory = loggerFactory,
            LogRequestBody = false,
            LogRequestHeaders = false,
            LogResponseHeaders = false,
        },
    };

    if (!string.IsNullOrWhiteSpace(payPalOptions.BaseUrl))
    {
        // When set, used verbatim as the API base for every call — including the OAuth token request
        // (the token URL resolves through this same base).
        clientOptions.Server.Default.Sandbox.BaseUrl = payPalOptions.BaseUrl;
    }

    return new PayPalServerSdkClient(httpClient, clientOptions);
});

builder.Services.AddScoped<IPayPalGateway, PayPalGateway>();
builder.Services.AddScoped<IPaymentService, PaymentService>();

var configSection = builder.Configuration.GetRequiredSection(BaseUrlConfiguration.CONFIG_NAME);
builder.Services.Configure<BaseUrlConfiguration>(configSection);
var baseUrlConfig = configSection.Get<BaseUrlConfiguration>();

builder.Services.AddMemoryCache();

var key = Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
builder.Services.AddAuthentication(config =>
{
    config.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(config =>
{
    config.RequireHttpsMetadata = false;
    config.SaveToken = true;
    config.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false
    };
});

const string CORS_POLICY = "CorsPolicy";
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: CORS_POLICY,
        corsPolicyBuilder =>
        {
            corsPolicyBuilder.WithOrigins(baseUrlConfig!.WebBase.Replace("host.docker.internal", "localhost").TrimEnd('/'));
            corsPolicyBuilder.AllowAnyMethod();
            corsPolicyBuilder.AllowAnyHeader();
        });
});

builder.Services.AddControllers();
builder.Services.AddAutoMapper(typeof(MappingProfile).Assembly);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "My API", Version = "v1" });
    c.EnableAnnotations();
    c.SchemaFilter<CustomSchemaFilters>();
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = @"JWT Authorization header using the Bearer scheme. \r\n\r\n 
                      Enter 'Bearer' [space] and then your token in the text input below.
                      \r\n\r\nExample: 'Bearer 12345abcdef'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement()
            {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            },
                            Scheme = "oauth2",
                            Name = "Bearer",
                            In = ParameterLocation.Header,

                        },
                        new List<string>()
                    }
            });
});

var app = builder.Build();

app.Logger.LogInformation("PublicApi App created...");

app.Logger.LogInformation("Seeding Database...");

using (var scope = app.Services.CreateScope())
{
    var scopedProvider = scope.ServiceProvider;
    try
    {
        var catalogContext = scopedProvider.GetRequiredService<CatalogContext>();
        await CatalogContextSeed.SeedAsync(catalogContext, app.Logger);

        var userManager = scopedProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scopedProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var identityContext = scopedProvider.GetRequiredService<AppIdentityDbContext>();
        await AppIdentityDbContextSeed.SeedAsync(identityContext, userManager, roleManager);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "An error occurred seeding the DB.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseMiddleware<ExceptionMiddleware>();

app.UseHttpsRedirection();

app.UseRouting();

app.UseCors(CORS_POLICY);

app.UseAuthorization();

// Enable middleware to serve generated Swagger as a JSON endpoint.
app.UseSwagger();

// Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.), 
// specifying the Swagger JSON endpoint.
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
});

app.MapControllers();
app.MapEndpoints();

app.Logger.LogInformation("LAUNCHING PublicApi");
app.Run();

public partial class Program { }
