using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
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
using Microsoft.eShopWeb.PublicApi.MaxioBilling;
using Microsoft.eShopWeb.PublicApi.MaxioBillingEndpoints;
using Microsoft.eShopWeb.PublicApi.Middleware;
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

// Maxio Billing
builder.Services.Configure<MaxioSettings>(builder.Configuration.GetRequiredSection(MaxioSettings.CONFIG_NAME));
builder.Services.AddHttpClient<IMaxioClient, MaxioClient>();
builder.Services.AddHttpContextAccessor();

var key = Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
builder.Services.AddAuthentication(config =>
{
    config.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    config.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
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

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
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

// Maxio Subscription Billing Endpoints
app.MapGet("api/subscription-plans", async (IMaxioClient maxioClient, IOptions<MaxioSettings> settings) =>
{
    var response = new SubscriptionPlanListResponse();
    var products = await maxioClient.ListProductsAsync(settings.Value.ProductFamilyHandle);
    response.Plans = products
        .Where(p => p.ArchivedAt == null)
        .Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Name = p.Name,
            Handle = p.Handle ?? string.Empty,
            Description = p.Description ?? string.Empty,
            PriceInCents = p.PriceInCents,
            PriceDisplay = $"${p.PriceInCents / 100.0:F2}",
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
            TrialPriceInCents = p.TrialPriceInCents,
            TrialInterval = p.TrialInterval,
            TrialIntervalUnit = p.TrialIntervalUnit,
            RequireCreditCard = p.RequireCreditCard,
            Taxable = p.Taxable,
            ProductFamilyName = p.ProductFamily?.Name ?? string.Empty,
            ProductFamilyHandle = p.ProductFamily?.Handle ?? string.Empty
        })
        .ToList();
    return Results.Ok(response);
})
.WithTags("SubscriptionEndpoints");

app.MapPost("api/subscriptions", async (SubscriptionCreateRequest request, IMaxioClient maxioClient, HttpContext httpContext) =>
{
    var response = new SubscriptionCreateResponse(request.CorrelationId());

    var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value
        ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? httpContext.User.FindFirst("sub")?.Value;

    if (string.IsNullOrEmpty(userName))
        return Results.Unauthorized();

    var firstName = httpContext.User.FindFirst("given_name")?.Value
        ?? httpContext.User.FindFirst(ClaimTypes.GivenName)?.Value
        ?? "User";
    var lastName = httpContext.User.FindFirst("family_name")?.Value
        ?? httpContext.User.FindFirst(ClaimTypes.Surname)?.Value
        ?? "Subscriber";
    var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value
        ?? httpContext.User.FindFirst("email")?.Value
        ?? userName;

    var customerRef = $"eshop-{userName}";
    var customer = await maxioClient.LookupCustomerByReferenceAsync(customerRef);
    if (customer == null)
    {
        customer = await maxioClient.CreateCustomerAsync(new MaxioCreateCustomerRequest
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Reference = customerRef
        });
    }

    var existingSubscriptions = await maxioClient.ListCustomerSubscriptionsAsync(customer.Id);
    var existingSub = existingSubscriptions.Find(s =>
        s.Product?.Handle == request.ProductHandle &&
        s.State is "active" or "trialing" or "pending");

    if (existingSub != null)
    {
        response.Subscription = SubscriptionDtoMapper.MapToDto(existingSub);
        response.AlreadySubscribed = true;
        return Results.Ok(response);
    }

    var createRequest = new MaxioCreateSubscriptionRequest
    {
        Subscription = new MaxioSubscriptionPayload
        {
            ProductHandle = request.ProductHandle,
            CustomerId = customer.Id,
            PaymentCollectionMethod = "invoice"
        }
    };

    var newSub = await maxioClient.CreateSubscriptionAsync(createRequest);
    response.Subscription = SubscriptionDtoMapper.MapToDto(newSub);
    response.AlreadySubscribed = false;
    return Results.Created($"api/my-subscriptions/{newSub.Id}", response);
})
.RequireAuthorization()
.WithTags("SubscriptionEndpoints");

app.MapGet("api/my-subscriptions", async (IMaxioClient maxioClient, HttpContext httpContext) =>
{
    var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value
        ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? httpContext.User.FindFirst("sub")?.Value;

    if (string.IsNullOrEmpty(userName))
        return Results.Unauthorized();

    var customerRef = $"eshop-{userName}";
    var customer = await maxioClient.LookupCustomerByReferenceAsync(customerRef);

    var response = new MySubscriptionListResponse();
    if (customer == null)
    {
        response.Subscriptions = new List<SubscriptionDto>();
        return Results.Ok(response);
    }

    var subscriptions = await maxioClient.ListCustomerSubscriptionsAsync(customer.Id);
    response.Subscriptions = subscriptions
        .Select(s => SubscriptionDtoMapper.MapToDto(s))
        .ToList();

    return Results.Ok(response);
})
.RequireAuthorization()
.WithTags("SubscriptionEndpoints");

app.Logger.LogInformation("LAUNCHING PublicApi");
app.Run();

public partial class Program { }

namespace Microsoft.eShopWeb.PublicApi
{
    internal static class SubscriptionDtoMapper
    {
        internal static SubscriptionDto MapToDto(MaxioSubscription sub) => new()
        {
            Id = sub.Id,
            State = sub.State,
            PlanName = sub.Product?.Name ?? string.Empty,
            PlanHandle = sub.Product?.Handle ?? string.Empty,
            PriceInCents = sub.ProductPriceInCents,
            PriceDisplay = $"${sub.ProductPriceInCents / 100.0:F2}",
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            NextBillingDate = sub.NextAssessmentAt,
            ActivatedAt = sub.ActivatedAt,
            CreatedAt = sub.CreatedAt,
            CanceledAt = sub.CanceledAt,
            TotalRevenueInCents = sub.TotalRevenueInCents,
            BalanceInCents = sub.BalanceInCents
        };
    }
}
