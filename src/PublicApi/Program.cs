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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Logging;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.eShopWeb.PublicApi;
using Microsoft.eShopWeb.PublicApi.Middleware;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
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

// Configure Maxio Advanced Billing
builder.Services.Configure<MaxioOptions>(builder.Configuration.GetSection(MaxioOptions.ConfigurationSection));
builder.Services.AddHttpClient<IMaxioService, MaxioService>();

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

// Subscription endpoints registered directly (Ardalis endpoints have issues with auth)
var subscriptionGroup = app.MapGroup("/api").WithTags("SubscriptionEndpoints");

subscriptionGroup.MapGet("/subscription-plans", async (IMaxioService maxioService) =>
{
    var products = await maxioService.GetProductsAsync();
    var plans = products.Select(p => new SubscriptionPlanDto
    {
        Id = p.Id,
        Name = p.Name,
        Handle = p.Handle,
        Description = p.Description,
        PriceInCents = p.PriceInCents,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit,
        RequireCreditCard = p.RequireCreditCard,
        Taxable = p.Taxable,
        ProductFamilyName = p.ProductFamily?.Name,
        ProductFamilyHandle = p.ProductFamily?.Handle
    }).ToList();
    return Results.Ok(new { IsSuccess = true, Plans = plans });
});

subscriptionGroup.MapPost("/subscriptions", async (CreateSubscriptionRequest request, HttpContext httpContext, IMaxioService maxioService) =>
{
    var userId = httpContext.User.FindFirstValue(ClaimTypes.Name)
        ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? httpContext.User.FindFirstValue("sub")
        ?? httpContext.User.FindFirstValue("unique_name");

    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

    request.UserReference = userId;

    try
    {
        var customer = await maxioService.EnsureCustomerAsync(
            request.UserReference, request.FirstName, request.LastName, request.Email);
        var subscription = await maxioService.CreateSubscriptionAsync(customer.Id, request.ProductHandle);

        return Results.Ok(new CreateSubscriptionResponse
        {
            IsSuccess = true,
            Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                State = subscription.State,
                ProductHandle = subscription.Product?.Handle ?? subscription.ProductHandle,
                ProductName = subscription.Product?.Name ?? subscription.ProductName,
                ProductPriceInCents = subscription.ProductPriceInCents,
                CreatedAt = subscription.CreatedAt,
                CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                Customer = new CustomerDto
                {
                    Id = customer.Id,
                    FirstName = customer.FirstName,
                    LastName = customer.LastName,
                    Email = customer.Email,
                    Reference = customer.Reference
                }
            }
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new CreateSubscriptionResponse
        {
            IsSuccess = false,
            ErrorMessage = $"Failed to create subscription: {ex.Message}"
        });
    }
}).Produces<CreateSubscriptionResponse>().RequireAuthorization();

subscriptionGroup.MapGet("/my-subscriptions", async (HttpContext httpContext, IMaxioService maxioService) =>
{
    var userId = httpContext.User.FindFirstValue(ClaimTypes.Name)
        ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? httpContext.User.FindFirstValue("sub")
        ?? httpContext.User.FindFirstValue("unique_name");

    if (string.IsNullOrEmpty(userId))
    {
        return Results.Unauthorized();
    }

    var response = new GetMySubscriptionsResponse();

    try
    {
        var subscriptions = await maxioService.GetSubscriptionsByCustomerReferenceAsync(userId);
        response.IsSuccess = true;
        response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            State = s.State,
            ProductHandle = s.Product?.Handle ?? s.ProductHandle,
            ProductName = s.Product?.Name ?? s.ProductName,
            ProductPriceInCents = s.ProductPriceInCents,
            CreatedAt = s.CreatedAt,
            CurrentPeriodStartsAt = s.CurrentPeriodStartsAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextAssessmentAt = s.NextAssessmentAt,
            Customer = s.Customer != null ? new CustomerDto
            {
                Id = s.Customer.Id,
                FirstName = s.Customer.FirstName,
                LastName = s.Customer.LastName,
                Email = s.Customer.Email,
                Reference = s.Customer.Reference
            } : null
        }).ToList();
    }
    catch (Exception ex)
    {
        response.IsSuccess = false;
        response.ErrorMessage = $"Failed to retrieve subscriptions: {ex.Message}";
    }

    return Results.Ok(response);
}).Produces<GetMySubscriptionsResponse>().RequireAuthorization();

app.Logger.LogInformation("LAUNCHING PublicApi");
app.Run();

public partial class Program { }
