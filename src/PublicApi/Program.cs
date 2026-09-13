using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using BlazorShared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

// Maxio configuration
builder.Services.Configure<MaxioOptions>(builder.Configuration.GetSection(MaxioOptions.SectionName));
builder.Services.AddHttpClient();
builder.Services.AddScoped<IMaxioService, MaxioService>();

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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("JwtOnly", policy =>
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
              .RequireAuthenticatedUser());
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
builder.Configuration.AddUserSecrets<Program>(optional: true);

// Map MAXIO_* env vars to Maxio section config keys
var maxioApiKey = builder.Configuration["MAXIO_API_KEY"];
var maxioSubdomain = builder.Configuration["MAXIO_SITE_SUBDOMAIN"];
var maxioEnvironment = builder.Configuration["MAXIO_ENVIRONMENT"];
var maxioProductFamily = builder.Configuration["MAXIO_DEFAULT_PRODUCT_FAMILY"];
var maxioBaseUrl = builder.Configuration["MAXIO_BASE_URL"];

if (!string.IsNullOrEmpty(maxioApiKey) || !string.IsNullOrEmpty(maxioSubdomain))
{
    var maxioOverrides = new Dictionary<string, string?>
    {
        { "Maxio:ApiKey", maxioApiKey ?? builder.Configuration["Maxio:ApiKey"] },
        { "Maxio:Subdomain", maxioSubdomain ?? builder.Configuration["Maxio:Subdomain"] },
        { "Maxio:ProductFamilyHandle", maxioProductFamily ?? builder.Configuration["Maxio:ProductFamilyHandle"] },
        { "Maxio:BaseUrl", maxioBaseUrl ?? builder.Configuration["Maxio:BaseUrl"] }
    };
    builder.Configuration.AddInMemoryCollection(maxioOverrides);
}

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

// Subscription billing endpoints (registered directly for auth compatibility)
app.MapGet("api/subscription-plans", async (IMaxioService maxioService) =>
{
    var plans = await maxioService.ListPlansAsync();
    return Results.Ok(new ListSubscriptionPlansResponse { Plans = plans });
})
.WithTags("SubscriptionEndpoints")
.RequireAuthorization("JwtOnly");

app.MapPost("api/subscriptions", async (CreateSubscriptionRequest request, Microsoft.AspNetCore.Http.HttpContext httpContext, IMaxioService maxioService) =>
{
    if (string.IsNullOrWhiteSpace(request.ProductHandle))
    {
        return Results.BadRequest(new { error = "ProductHandle is required." });
    }

    var customerReference = httpContext.User.FindFirstValue(System.Security.Claims.ClaimTypes.Name);
    if (string.IsNullOrWhiteSpace(customerReference))
    {
        return Results.Unauthorized();
    }

    var email = $"{customerReference}@eshop.local";
    var subscription = await maxioService.SubscribeAsync(
        request.ProductHandle,
        customerReference,
        customerReference,
        "",
        email);

    return Results.Ok(new CreateSubscriptionResponse { Subscription = subscription });
})
.WithTags("SubscriptionEndpoints")
.RequireAuthorization("JwtOnly");

app.MapGet("api/my-subscriptions", async (HttpContext httpContext, IMaxioService maxioService) =>
{
    var customerReference = httpContext.User.FindFirstValue(System.Security.Claims.ClaimTypes.Name);
    if (string.IsNullOrWhiteSpace(customerReference))
    {
        return Results.Unauthorized();
    }

    var subscriptions = await maxioService.ListMySubscriptionsAsync(customerReference);
    return Results.Ok(new ListMySubscriptionsResponse { Subscriptions = subscriptions });
})
.WithTags("SubscriptionEndpoints")
.RequireAuthorization("JwtOnly");

app.Logger.LogInformation("LAUNCHING PublicApi");
app.Run();

public partial class Program { }
