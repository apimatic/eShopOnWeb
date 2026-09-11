using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
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
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
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

var configSection = builder.Configuration.GetRequiredSection(BaseUrlConfiguration.CONFIG_NAME);
builder.Services.Configure<BaseUrlConfiguration>(configSection);
var baseUrlConfig = configSection.Get<BaseUrlConfiguration>();

builder.Services.AddMemoryCache();

// Maxio billing integration
builder.Services.Configure<MaxioOptions>(builder.Configuration.GetRequiredSection(MaxioOptions.SectionName));
builder.Services.AddHttpClient<IMaxioHttpClient, MaxioHttpClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioOptions>>().Value;
    var baseUrl = !string.IsNullOrEmpty(opts.BaseUrl)
        ? opts.BaseUrl.TrimEnd('/')
        : $"https://{opts.Subdomain}.chargify.com";
    client.BaseAddress = new Uri(baseUrl + "/");
    var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{opts.ApiKey}:x"));
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});
builder.Services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

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

// Map MAXIO_* env vars to Maxio: config section
var maxioApiKey = Environment.GetEnvironmentVariable("MAXIO_API_KEY");
var maxioSubdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
var maxioProductFamily = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY");
var maxioBaseUrl = Environment.GetEnvironmentVariable("MAXIO_BASE_URL");
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Maxio:ApiKey"] = maxioApiKey,
    ["Maxio:Subdomain"] = maxioSubdomain,
    ["Maxio:ProductFamilyHandle"] = maxioProductFamily,
    ["Maxio:BaseUrl"] = maxioBaseUrl
});

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

// Diagnostic: check claims
app.MapGet("api/debug-claims", (HttpContext ctx) =>
{
    var user = ctx.User;
    var claims = user.Claims.Select(c => new { c.Type, c.Value }).ToList();
    return Results.Ok(new { isAuthenticated = user.Identity?.IsAuthenticated ?? false, claims });
});

// Subscription billing endpoints
app.MapGet("api/subscription-plans", async (IMaxioSubscriptionService subscriptionService) =>
{
    var plans = await subscriptionService.GetAvailablePlansAsync();
    var dtos = plans.Select(p => new SubscriptionPlanDto
    {
        Id = p.Id,
        Name = p.Name,
        Handle = p.Handle ?? string.Empty,
        Description = p.Description,
        Price = p.Price,
        Interval = p.IntervalDisplay,
        RequireCreditCard = p.RequireCreditCard,
        Taxable = p.Taxable
    }).ToList();
    return Results.Ok(new ListSubscriptionPlansResponse { Plans = dtos, IsSuccess = true });
})
.WithTags("SubscriptionEndpoints");

app.MapPost("api/subscriptions", async (HttpRequest httpRequest, IMaxioSubscriptionService subscriptionService) =>
{
    var userName = httpRequest.HttpContext.User.FindFirstValue(ClaimTypes.Name);
    if (string.IsNullOrEmpty(userName))
        return Results.Json(new { isSuccess = false, errorMessage = "Authentication required." }, statusCode: 401);

    var userEmail = httpRequest.HttpContext.User.FindFirstValue(ClaimTypes.Email) ?? $"{userName}@placeholder.local";

    var body = await httpRequest.ReadFromJsonAsync<CreateSubscriptionRequest>();
    if (body == null || string.IsNullOrEmpty(body.ProductHandle))
        return Results.Json(new { isSuccess = false, errorMessage = "ProductHandle is required." }, statusCode: 400);

    try
    {
        var subscription = await subscriptionService.SubscribeAsync(userName, userName, userName, userEmail, body.ProductHandle);
        return Results.Ok(new CreateSubscriptionResponse
        {
            Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                State = subscription.State,
                ProductName = subscription.Product?.Name ?? string.Empty,
                ProductHandle = subscription.Product?.Handle ?? body.ProductHandle,
                ProductPrice = subscription.ProductPrice,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt,
                CanceledAt = subscription.CanceledAt,
                CreatedAt = subscription.CreatedAt,
                Currency = subscription.Currency
            },
            IsSuccess = true
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new CreateSubscriptionResponse { IsSuccess = false, ErrorMessage = ex.Message });
    }
})
.WithTags("SubscriptionEndpoints");

app.MapGet("api/my-subscriptions", async (IMaxioSubscriptionService subscriptionService, HttpContext httpContext) =>
{
    var userName = httpContext.User.FindFirstValue(ClaimTypes.Name);
    if (string.IsNullOrEmpty(userName))
        return Results.Json(new { isSuccess = false, errorMessage = "Authentication required." }, statusCode: 401);

    try
    {
        var subscriptions = await subscriptionService.GetMySubscriptionsAsync(userName);
        var dtos = subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            State = s.State,
            ProductName = s.Product?.Name ?? string.Empty,
            ProductHandle = s.Product?.Handle ?? string.Empty,
            ProductPrice = s.ProductPrice,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextAssessmentAt = s.NextAssessmentAt,
            ActivatedAt = s.ActivatedAt,
            CanceledAt = s.CanceledAt,
            CreatedAt = s.CreatedAt,
            Currency = s.Currency
        }).ToList();
        return Results.Ok(new ListMySubscriptionsResponse { Subscriptions = dtos, IsSuccess = true });
    }
    catch (Exception ex)
    {
        return Results.Ok(new ListMySubscriptionsResponse { IsSuccess = false, ErrorMessage = ex.Message });
    }
})
.WithTags("SubscriptionEndpoints");

app.Logger.LogInformation("LAUNCHING PublicApi");
app.Run();

public partial class Program { }
