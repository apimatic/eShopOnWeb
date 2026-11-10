using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

internal static class Auth
{
    public const string Scheme = JwtBearerDefaults.AuthenticationScheme;
    public const string Admin = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public static string Buyer(ClaimsPrincipal user) =>
        user.Identity?.Name ?? throw new AppException(401, "Not signed in.");
}

public class BankListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/banks",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (string? query, BankDirectory directory, CancellationToken ct) =>
                Results.Ok(new { banks = await directory.SearchAsync(query, ct) }))
            .WithTags("BankEndpoints");
    }
}

public class BankConnectionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/link-token",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (LinkTokenRequest? request, ClaimsPrincipal user, ConnectionService svc, CancellationToken ct) =>
                Results.Ok(await svc.CreateLinkTokenAsync(Auth.Buyer(user), request?.Kind, ct)))
            .WithTags("BankEndpoints");

        app.MapPost("api/bank/connections",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (ConnectRequest request, ClaimsPrincipal user, ConnectionService svc, CancellationToken ct) =>
            {
                var conn = await svc.ConnectAsync(Auth.Buyer(user), request, ct);
                return Results.Created($"api/bank/connections/{conn.Id}", ConnectionService.ToDto(conn));
            })
            .WithTags("BankEndpoints");

        app.MapGet("api/bank/connections",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (ClaimsPrincipal user, ConnectionService svc, CancellationToken ct) =>
            {
                var list = await svc.ListAsync(Auth.Buyer(user), ct);
                return Results.Ok(new { connections = list.ConvertAll(ConnectionService.ToDto) });
            })
            .WithTags("BankEndpoints");

        app.MapDelete("api/bank/connections/{connectionId}",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (Guid connectionId, ClaimsPrincipal user, ConnectionService svc, CancellationToken ct) =>
            {
                await svc.DisconnectAsync(Auth.Buyer(user), connectionId, ct);
                return Results.NoContent();
            })
            .WithTags("BankEndpoints");
    }
}

public class BankPaymentEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (StartPaymentRequest request, ClaimsPrincipal user, PaymentService svc, CancellationToken ct) =>
                Results.Ok(await svc.StartAsync(Auth.Buyer(user), request, ct)))
            .WithTags("CheckoutEndpoints");

        app.MapPut("api/checkout/bank-payment/{checkoutId}/shipping",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (Guid checkoutId, ShippingRequest request, ClaimsPrincipal user, PaymentService svc, CancellationToken ct) =>
                Results.Ok(await svc.ChangeShippingAsync(Auth.Buyer(user), checkoutId, request, ct)))
            .WithTags("CheckoutEndpoints");

        app.MapPost("api/checkout/bank-payment/{checkoutId}/confirm",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (Guid checkoutId, ConfirmRequest request, ClaimsPrincipal user, PaymentService svc, CancellationToken ct) =>
                Results.Ok(await svc.ConfirmAsync(Auth.Buyer(user), checkoutId, request, ct)))
            .WithTags("CheckoutEndpoints");

        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (int orderId, ClaimsPrincipal user, PaymentService svc, CancellationToken ct) =>
                Results.Ok(await svc.CancelOrderAsync(Auth.Buyer(user), orderId, ct)))
            .WithTags("CheckoutEndpoints");
    }
}

public class SupportEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/bank-activity",
            [Authorize(Roles = Auth.Admin, AuthenticationSchemes = Auth.Scheme)] async (string email, UserManager<ApplicationUser> users, ActivityService svc, CancellationToken ct) =>
                Results.Ok(await svc.BankActivityAsync(await ShopperAsync(users, email), ct)))
            .WithTags("SupportEndpoints");

        app.MapGet("api/support/shoppers/{email}/transactions",
            [Authorize(Roles = Auth.Admin, AuthenticationSchemes = Auth.Scheme)] async (string email, string? from, string? to, UserManager<ApplicationUser> users, ActivityService svc, CancellationToken ct) =>
            {
                var f = ParseDate(from, "from");
                var t = ParseDate(to, "to");
                if (f > t) throw new AppException(400, "'from' must not be after 'to'.");
                return Results.Ok(await svc.TransactionsBetweenAsync(await ShopperAsync(users, email), f, t, ct));
            })
            .WithTags("SupportEndpoints");
    }

    private static DateOnly ParseDate(string? value, string name) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : throw new AppException(400, $"'{name}' is required and must be a date as YYYY-MM-DD.");

    private static async Task<string> ShopperAsync(UserManager<ApplicationUser> users, string email)
    {
        var user = await users.FindByEmailAsync(email) ?? await users.FindByNameAsync(email);
        return user?.UserName ?? throw new AppException(404, "No shopper with that email.");
    }
}

public class PortfolioEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/portfolio",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (ClaimsPrincipal user, ActivityService svc, CancellationToken ct) =>
                Results.Ok(await svc.PortfolioAsync(Auth.Buyer(user), ct)))
            .WithTags("PortfolioEndpoints");
    }
}
