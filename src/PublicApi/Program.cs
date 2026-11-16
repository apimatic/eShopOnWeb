using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using BlazorShared;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using FinixApiReference.Core.Hooks;
using FinixApiReference.Servers;
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
using Microsoft.eShopWeb.PublicApi.Payments;
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

var configSection = builder.Configuration.GetRequiredSection(BaseUrlConfiguration.CONFIG_NAME);
builder.Services.Configure<BaseUrlConfiguration>(configSection);
var baseUrlConfig = configSection.Get<BaseUrlConfiguration>();

builder.Services.AddMemoryCache();

var key = Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
builder.Services.AddAuthentication(config =>
{
    config.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    config.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    config.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    config.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
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

var finixEnvironmentValues = new Dictionary<string, string?>();
void MapFinixEnvironmentVariable(string environmentVariable, string configurationKey)
{
    var value = Environment.GetEnvironmentVariable(environmentVariable);
    if (!string.IsNullOrWhiteSpace(value))
    {
        finixEnvironmentValues[configurationKey] = value;
    }
}

MapFinixEnvironmentVariable("FINIX_USERNAME", "Finix:Username");
MapFinixEnvironmentVariable("FINIX_PASSWORD", "Finix:Password");
MapFinixEnvironmentVariable("FINIX_MERCHANT_ID", "Finix:MerchantId");
MapFinixEnvironmentVariable("FINIX_ENVIRONMENT", "Finix:Environment");
MapFinixEnvironmentVariable("FINIX_CURRENCY", "Finix:Currency");

if (finixEnvironmentValues.Count > 0)
{
    builder.Configuration.AddInMemoryCollection(finixEnvironmentValues);
}

var finixSection = builder.Configuration.GetSection(FinixOptions.SectionName);
builder.Services
    .AddOptions<FinixOptions>()
    .Bind(finixSection)
    .ValidateDataAnnotations()
    .Validate(options => options.Environment is "sandbox" or "live",
        "Finix:Environment must be either 'sandbox' or 'live'.")
    .Validate(options => options.Environment != "live" || !string.IsNullOrWhiteSpace(options.BaseUrl),
        "Finix:BaseUrl is required when Finix:Environment is 'live'.")
    .Validate(options => options.Currency is "USD" or "CAD",
        "Finix:Currency must be a currency this integration supports (USD or CAD).")
    .ValidateOnStart();

builder.Services.AddFinixApiReferenceClient(options =>
{
    var finix = finixSection.Get<FinixOptions>() ?? new FinixOptions();

    options.BasicAuth = new BasicAuthCredentials
    {
        Username = finix.Username,
        Password = finix.Password
    };
    options.Environment = ServerEnvironment.Production;

    if (!string.IsNullOrWhiteSpace(finix.BaseUrl))
    {
        options.Server.Default.Production.BaseUrl = finix.BaseUrl;
    }

    options.Retry = RetryOptions.Default() with
    {
        HttpMethodsToRetry = new[] { HttpMethod.Get, HttpMethod.Head },
        MaxRetries = 2,
        Timeout = TimeSpan.FromSeconds(15)
    };

    options.Logging = new LoggingOptions
    {
        LogRequestBody = false,
        LogRequestHeaders = false,
        LogResponseHeaders = false
    };

    options.Hooks = new[]
    {
        SdkHook.OnRequest((request, _) => NormalizeFinixDateQuery(request))
    };
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<FinixGateway>();
builder.Services.AddScoped<PaymentService>();

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

app.UseAuthentication();
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

static void NormalizeFinixDateQuery(HttpRequestMessage request)
{
    var uri = request.RequestUri;
    if (uri is null || string.IsNullOrEmpty(uri.Query))
    {
        return;
    }

    var parts = uri.Query.TrimStart('?').Split('&');
    var changed = false;

    for (var index = 0; index < parts.Length; index++)
    {
        var separator = parts[index].IndexOf('=');
        if (separator <= 0)
        {
            continue;
        }

        var key = parts[index][..separator];
        if (key is not ("created_at.gte" or "created_at.lte" or "created_at"
            or "updated_at.gte" or "updated_at.lte"
            or "ready_to_settle_at.gte" or "ready_to_settle_at.lte"))
        {
            continue;
        }

        var rawValue = Uri.UnescapeDataString(parts[index][(separator + 1)..]);
        if (!DateTimeOffset.TryParse(
                rawValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            continue;
        }

        parts[index] = $"{key}={Uri.EscapeDataString(parsed.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))}";
        changed = true;
    }

    if (!changed)
    {
        return;
    }

    var builder = new UriBuilder(uri)
    {
        Query = string.Join('&', parts)
    };
    request.RequestUri = builder.Uri;
}

public partial class Program { }
