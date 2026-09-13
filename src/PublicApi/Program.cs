using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using BlazorShared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Logging;
using Microsoft.eShopWeb.PublicApi;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MinimalApi.Endpoint.Configurations.Extensions;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using AutoMapper;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddHttpContextAccessor();

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

// Maxio Advanced Billing
builder.Services.Configure<MaxioOptions>(builder.Configuration.GetRequiredSection(MaxioOptions.SectionName));
builder.Services.AddSingleton<MaxioAdvancedBillingClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var options = config.GetRequiredSection(MaxioOptions.SectionName).Get<MaxioOptions>()
        ?? throw new InvalidOperationException("Maxio configuration section is missing.");

    var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    var clientOptions = new MaxioAdvancedBillingClientOptions
    {
        BasicAuth = new BasicAuthCredentials
        {
            Username = options.ApiKey,
            Password = "x"
        },
        Environment = ServerEnvironment.Us
    };

    if (!string.IsNullOrEmpty(options.BaseUrl))
    {
        clientOptions.Server.Production.Us.BaseUrl = options.BaseUrl;
    }
    else if (!string.IsNullOrEmpty(options.Subdomain))
    {
        clientOptions.Server.Production.Us.Site = options.Subdomain;
    }

    return new MaxioAdvancedBillingClient(httpClient, clientOptions);
});
builder.Services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

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

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
});

app.MapControllers();

// === Maxio Subscription Endpoints ===

app.MapGet("api/subscription-plans", async (IMaxioSubscriptionService maxioService) =>
{
    var plans = await maxioService.ListPlansAsync();
    return Results.Ok(new { Plans = plans });
})
.WithTags("MaxioEndpoints")
.Produces<object>();

app.MapPost("api/subscriptions",
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    async (SubscribeRequest request, HttpContext httpContext, IMaxioSubscriptionService maxioService) =>
{
    var userReference = httpContext.User.FindFirstValue(ClaimTypes.Name);
    if (string.IsNullOrEmpty(userReference))
        return Results.Unauthorized();

    try
    {
        var subscription = await maxioService.SubscribeAsync(userReference, request.ProductHandle);
        return Results.Ok(new { Subscription = subscription });
    }
    catch (MaxioSubscriptionException ex)
    {
        return Results.Json(new { StatusCode = (int)ex.StatusCode, Message = ex.Message }, statusCode: (int)ex.StatusCode);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { Message = ex.Message });
    }
})
.WithTags("MaxioEndpoints")
.Produces<object>()
.RequireAuthorization();

app.MapGet("api/my-subscriptions",
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    async (HttpContext httpContext, IMaxioSubscriptionService maxioService) =>
{
    var userReference = httpContext.User.FindFirstValue(ClaimTypes.Name);
    if (string.IsNullOrEmpty(userReference))
        return Results.Unauthorized();

    var subscriptions = await maxioService.ListMySubscriptionsAsync(userReference);
    return Results.Ok(new { Subscriptions = subscriptions });
})
.WithTags("MaxioEndpoints")
.Produces<object>()
.RequireAuthorization();

app.Logger.LogInformation("LAUNCHING PublicApi");
app.Run();

public partial class Program { }

public record SubscribeRequest(string ProductHandle);
