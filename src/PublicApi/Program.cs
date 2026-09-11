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
using Microsoft.eShopWeb.PublicApi.Middleware;
using Microsoft.eShopWeb.PublicApi.Services.Maxio;
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
builder.Services.AddHttpClient<IMaxioService, MaxioService>();
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

// Manual subscription endpoints (parallel to endpoint classes)
app.MapGet("api/subscription-plans", async (IMaxioService maxio) =>
{
    var response = new { plans = new List<object>() };
    var handles = new[] { "eshop-pro", "basic-plan" };
    foreach (var h in handles)
    {
        var prod = await maxio.GetProductByHandleAsync(h);
        if (prod?.Product != null)
        {
            var price = prod.Product.PricePoints?.FirstOrDefault();
            var priceStr = price != null ? $"${price.PriceInCents / 100m:F2}/{price.IntervalUnit?.ToLower() ?? "mo"}" : "—";
            response.plans.Add(new { handle = prod.Product.Handle, name = prod.Product.Name, price = priceStr, familyHandle = prod.Product.ProductFamily?.Handle });
        }
    }
    return Results.Ok(response);
});//.RequireAuthorization();

app.MapPost("api/subscriptions", async (HttpContext ctx, IMaxioService maxio) =>
{
    var req = await ctx.Request.ReadFromJsonAsync<SubscribeRequest>();
    if (req == null || string.IsNullOrWhiteSpace(req.PlanHandle)) return Results.BadRequest(new { error = "planHandle required" });
    var user = ctx.User;
    var userId = user.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier) ?? "unknown";
    var email = user.FindFirstValue(System.Security.Claims.ClaimTypes.Email) ?? user.Identity?.Name ?? "user@eshop.local";
    var firstName = user.FindFirstValue(System.Security.Claims.ClaimTypes.GivenName) ?? "Customer";
    var lastName = user.FindFirstValue(System.Security.Claims.ClaimTypes.Surname) ?? "User";
    var customerRef = userId;
    var customer = await maxio.GetCustomerByReferenceAsync(customerRef);
    if (customer?.Customer == null)
    {
        var c = await maxio.CreateCustomerAsync(firstName, lastName, email, customerRef);
    }
    var subRef = $"sub-{userId}-{req.PlanHandle}";
    var existingSub = await maxio.GetSubscriptionByReferenceAsync(subRef);
    if (existingSub?.Subscription != null)
    {
        return Results.Ok(new { subscriptionId = existingSub.Subscription.Id, state = existingSub.Subscription.State, planHandle = existingSub.Subscription.ProductHandle, nextBillingAt = existingSub.Subscription.NextBillingAt, reference = existingSub.Subscription.Reference, created = false });
    }
    var createdSub = await maxio.CreateSubscriptionAsync(req.PlanHandle, customerRef, subRef);
    if (createdSub?.Subscription != null)
    {
        return Results.Ok(new { subscriptionId = createdSub.Subscription.Id, state = createdSub.Subscription.State, planHandle = createdSub.Subscription.ProductHandle, nextBillingAt = createdSub.Subscription.NextBillingAt, reference = createdSub.Subscription.Reference, created = true });
    }
    return Results.Ok(new { state = "failed" });
});

app.MapGet("api/my-subscriptions", async (HttpContext ctx, IMaxioService maxio) =>
{
    var userId = ctx.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier) ?? "unknown";
    var customer = await maxio.GetCustomerByReferenceAsync(userId);
    var list = new List<object>();
    if (customer?.Customer != null)
    {
        var subs = await maxio.ListCustomerSubscriptionsAsync(customer.Customer.Id);
        if (subs != null)
        {
            foreach (var s in subs)
            {
                if (s?.Subscription != null)
                    list.Add(new { id = s.Subscription.Id, state = s.Subscription.State, planHandle = s.Subscription.ProductHandle, nextBillingAt = s.Subscription.NextBillingAt, reference = s.Subscription.Reference });
            }
        }
    }
    return Results.Ok(new { subscriptions = list });
});

app.Logger.LogInformation("LAUNCHING PublicApi");
app.Run();

public class SubscribeRequest { public string PlanHandle { get; set; } = string.Empty; }
public partial class Program { }
