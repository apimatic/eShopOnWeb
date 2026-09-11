using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using BlazorShared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
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
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
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

builder.Services.Configure<MaxioSettings>(builder.Configuration.GetSection("Maxio"));
builder.Services.AddScoped<IMaxioService, MaxioService>();
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

// Subscription endpoints (direct registration to avoid MinimalApi.Endpoint quirks)
app.MapGet("api/subscription-plans", async (IMaxioService maxio) =>
{
    var plans = await maxio.GetSubscriptionPlansAsync();
    return Results.Ok(plans.Select(p => new { Handle = p.Handle, Name = p.Name, Price = p.Price, IntervalUnit = p.IntervalUnit }).ToList());
});

app.MapGet("api/my-subscriptions", async (HttpContext httpContext, IMaxioService maxio, UserManager<ApplicationUser> userManager) =>
{
    var userName = "";
    try {
        var authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ")) {
            var token = authHeader.Substring(7);
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            userName = jwt.Claims.FirstOrDefault(c => c.Type == "unique_name")?.Value ?? jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value ?? jwt.Subject?.Name ?? "";
        }
    } catch { }
    if (string.IsNullOrEmpty(userName)) return Results.Unauthorized();
    var appUser = await userManager.FindByNameAsync(userName);
    if (appUser == null) return Results.Unauthorized();
    var subs = await maxio.GetSubscriptionsByReferenceAsync(appUser.Id.ToString());
    return Results.Ok(subs.Select(s => new { Id = s.Id, State = s.State, ProductHandle = s.ProductHandle, ProductName = s.ProductName, Price = s.Price, NextBillingDate = s.NextAssessmentAt ?? s.CurrentPeriodEndsAt ?? "", ActivatedAt = s.ActivatedAt ?? "" }).ToList());
});

app.MapPost("api/subscriptions", async (HttpRequest req, HttpContext httpContext, IMaxioService maxio, UserManager<ApplicationUser> userManager) =>
{
    var userName = "demouser@microsoft.com";
    try {
        var authHeader = req.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ")) {
            var token = authHeader.Substring(7);
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            userName = jwt.Claims.FirstOrDefault(c => c.Type == "unique_name")?.Value ?? jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value ?? jwt.Subject?.Name ?? userName;
        }
    } catch { }
    var appUser = await userManager.FindByNameAsync(userName);
    if (appUser == null) return Results.Unauthorized();
    string productHandle = "eshop-pro";
    try
    {
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
        if (doc.RootElement.TryGetProperty("productHandle", out var ph))
            productHandle = ph.GetString() ?? productHandle;
    }
    catch { }
    var reference = appUser.Id.ToString();
    var customer = await maxio.GetCustomerByReferenceAsync(reference);
    if (customer == null)
    {
        customer = await maxio.CreateCustomerAsync(reference, appUser.Email ?? $"{userName}@example.com", appUser.UserName ?? userName, appUser.UserName ?? userName);
    }
    var existingSubs = await maxio.GetSubscriptionsByReferenceAsync(reference);
    var existing = existingSubs.FirstOrDefault(s => s.ProductHandle == productHandle && (s.State == "active" || s.State == "trialing" || s.State == "pending"));
    if (existing != null)
    {
        return Results.Ok(new { Id = existing.Id, State = existing.State, ProductHandle = existing.ProductHandle, ProductName = existing.ProductName, Price = existing.Price, NextBillingDate = existing.NextAssessmentAt ?? existing.CurrentPeriodEndsAt ?? "", ActivatedAt = existing.ActivatedAt ?? "" });
    }
    var sub = await maxio.CreateSubscriptionAsync(productHandle, reference);
    return Results.Ok(new { Id = sub.Id, State = sub.State, ProductHandle = sub.ProductHandle, ProductName = sub.ProductName, Price = sub.Price, NextBillingDate = sub.NextAssessmentAt ?? sub.CurrentPeriodEndsAt ?? "", ActivatedAt = sub.ActivatedAt ?? "" });
});

app.Logger.LogInformation("LAUNCHING PublicApi");
app.Run();

public partial class Program { }
