using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using BlazorShared;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

FinixConfiguration.AddEnvironmentFallbacks(builder.Configuration, builder.Configuration);

builder.Services
    .AddOptions<FinixSettings>()
    .Bind(builder.Configuration.GetSection("Finix"))
    .ValidateDataAnnotations()
    .Validate(s => string.Equals(s.Environment, "sandbox", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(s.Environment, "live", StringComparison.OrdinalIgnoreCase),
        "Finix:Environment must be 'sandbox' or 'live'.")
    .Validate(s => string.IsNullOrWhiteSpace(s.Currency) || FinixApiReference.Models.Enums.Currency.TryGetKnownValue(s.Currency, out _),
        "Finix:Currency must be a recognised ISO 4217 currency code.")
    .ValidateOnStart();

builder.Services.PostConfigure<FinixSettings>(settings =>
{
    if (string.IsNullOrWhiteSpace(settings.Environment))
    {
        settings.Environment = "sandbox";
    }

    if (string.IsNullOrWhiteSpace(settings.Currency))
    {
        settings.Currency = "USD";
    }
    else
    {
        settings.Currency = settings.Currency.Trim().ToUpperInvariant();
    }
});

builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<FinixSettings>>().Value);

var useOnlyInMemoryDatabase = false;
if (builder.Configuration["UseOnlyInMemoryDatabase"] is not null)
{
    useOnlyInMemoryDatabase = bool.Parse(builder.Configuration["UseOnlyInMemoryDatabase"]!);
}

if (useOnlyInMemoryDatabase)
{
    builder.Services.AddDbContext<PaymentsDbContext>(options => options.UseInMemoryDatabase("Payments"));
}
else
{
    var paymentsConnection = builder.Configuration.GetConnectionString("PaymentsConnection")
                             ?? builder.Configuration.GetConnectionString("CatalogConnection");
    builder.Services.AddDbContext<PaymentsDbContext>(options => options.UseSqlServer(paymentsConnection));
}

const string FinixHttpClientName = "FinixApiReference";

builder.Services
    .AddHttpClient(FinixHttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
    .AddHttpMessageHandler<FinixDateQueryHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    });

builder.Services.AddTransient<FinixDateQueryHandler>();

builder.Services.AddSingleton(sp =>
{
    var settings = sp.GetRequiredService<FinixSettings>();
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(FinixHttpClientName);

    var options = new FinixApiReferenceClientOptions
    {
        Environment = ServerEnvironment.Production,
        BasicAuth = new BasicAuthCredentials
        {
            Username = settings.Username,
            Password = settings.Password
        },
        Retry = RetryOptions.Default() with
        {
            Timeout = TimeSpan.FromSeconds(8),
            MaxRetries = 2
        },
        Logging = new LoggingOptions
        {
            LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
            LogRequestBody = false,
            LogRequestHeaders = false,
            LogResponseHeaders = false
        }
    };

    if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
    {
        options.Server.Default.Production.BaseUrl = settings.BaseUrl;
    }
    else if (string.Equals(settings.Environment, "live", StringComparison.OrdinalIgnoreCase))
    {
        options.Server.Default.Production.BaseUrl = "https://finix.live-payments-api.com";
    }

    return new FinixApiReferenceClient(httpClient, options);
});

builder.Services.AddSingleton<FinixGateway>();
builder.Services.AddScoped<PaymentClaimStore>();
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

        var paymentsContext = scopedProvider.GetRequiredService<PaymentsDbContext>();
        await paymentsContext.Database.EnsureCreatedAsync();
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

app.MapPaymentEndpoints();

app.MapControllers();
app.MapEndpoints();

app.Logger.LogInformation("LAUNCHING PublicApi");
app.Run();

public partial class Program { }
