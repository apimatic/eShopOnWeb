using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using BlazorShared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using MinimalApi.Endpoint.Configurations.Extensions;
using MinimalApi.Endpoint.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpoints();

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

builder.Services.AddAuthorization(config =>
{
    config.AddPolicy("Bearer", policy =>
        policy.RequireAuthenticatedUser()
              .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme));
});

// Maxio Advanced Billing
builder.Services.Configure<MaxioSettings>(builder.Configuration.GetSection(MaxioSettings.SectionName));
var maxioSettings = builder.Configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();

builder.Services.AddSingleton<MaxioAdvancedBillingClient>(sp =>
{
    var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    var environment = maxioSettings.Subdomain.Contains("ebilling", StringComparison.OrdinalIgnoreCase)
        ? ServerEnvironment.Eu
        : ServerEnvironment.Us;

    var options = new MaxioAdvancedBillingClientOptions
    {
        BasicAuth = new BasicAuthCredentials
        {
            Username = maxioSettings.ApiKey,
            Password = "x"
        },
        Environment = environment,
    };

    if (!string.IsNullOrEmpty(maxioSettings.Subdomain))
    {
        options.Server.Production.Us.Site = maxioSettings.Subdomain;
    }

    if (!string.IsNullOrEmpty(maxioSettings.BaseUrl))
    {
        options.Server.Production.Us.BaseUrl = maxioSettings.BaseUrl;
    }

    return new MaxioAdvancedBillingClient(httpClient, options);
});

builder.Services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

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
        Description = @"JWT Authorization header using the Bearer scheme.",
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

app.UseAuthentication();
app.UseRouting();

app.UseCors(CORS_POLICY);

app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
});

app.MapControllers();
app.MapEndpoints();

// Subscription endpoints (registered directly as minimal API endpoints)
app.MapGet("api/subscription-plans", async (ISubscriptionService subscriptionService, CancellationToken ct) =>
{
    var response = new ListSubscriptionPlansResponse();
    var plans = await subscriptionService.GetPlansAsync(ct);
    response.Plans.AddRange(plans);
    return Results.Ok(response);
})
.RequireAuthorization("Bearer")
.WithTags("SubscriptionEndpoints");

app.MapPost("api/subscriptions", async (CreateSubscriptionRequest request, ISubscriptionService subscriptionService, HttpContext httpContext) =>
{
    var userId = httpContext.User?.FindFirstValue(ClaimTypes.Name)
        ?? httpContext.User?.FindFirstValue("sub")
        ?? string.Empty;

    var response = new CreateSubscriptionResponse();

    if (string.IsNullOrEmpty(userId))
    {
        response.ErrorMessage = "User identity not found in token.";
        return Results.Unauthorized();
    }

    try
    {
        var subscription = await subscriptionService.SubscribeAsync(userId, request.PlanHandle);
        response.Subscription = subscription;
        return Results.Ok(response);
    }
    catch (InvalidOperationException ex)
    {
        response.ErrorMessage = ex.Message;
        return Results.BadRequest(response);
    }
})
.RequireAuthorization("Bearer")
.WithTags("SubscriptionEndpoints");

app.MapGet("api/my-subscriptions", async (ISubscriptionService subscriptionService, HttpContext httpContext) =>
{
    var userId = httpContext.User?.FindFirstValue(ClaimTypes.Name)
        ?? httpContext.User?.FindFirstValue("sub")
        ?? string.Empty;

    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

    var response = new MySubscriptionsResponse();
    var subscriptions = await subscriptionService.GetMySubscriptionsAsync(userId);
    response.Subscriptions.AddRange(subscriptions);
    return Results.Ok(response);
})
.RequireAuthorization("Bearer")
.WithTags("SubscriptionEndpoints");

app.Logger.LogInformation("LAUNCHING PublicApi");
app.Run();

public partial class Program { }
