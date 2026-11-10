using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

internal static class Auth
{
    public const string Shopper = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>The caller's identity always comes from the JWT.</summary>
    public static string Buyer(ClaimsPrincipal user)
    {
        var name = Plaid.Buyer.Normalize(user.Identity?.Name);
        return name.Length > 0 ? name : throw new ApiException(401, "The token carries no user name.");
    }
}

public class LinkTokenRequest { public string? Type { get; set; } }
public class SaveConnectionRequest { public string? PublicToken { get; set; } }
public class ChangeShippingRequest { public decimal ShippingCost { get; set; } }
public class ConfirmRequest { public string? Speed { get; set; } }

public class BankPickerEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/banks",
            [Authorize(AuthenticationSchemes = Auth.Shopper)] async (string? query, BankPickerService service) =>
            Results.Ok(new { banks = await service.SearchAsync(query) }))
            .WithTags("BankEndpoints");
    }
}

public class BankConnectionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/link-token",
            [Authorize(AuthenticationSchemes = Auth.Shopper)] async (LinkTokenRequest? request, ClaimsPrincipal user, BankConnectionService service) =>
            {
                var (token, expiration) = await service.CreateLinkTokenAsync(Auth.Buyer(user), request?.Type);
                return Results.Ok(new { linkToken = token, expiration });
            }).WithTags("BankEndpoints");

        app.MapPost("api/bank/connections",
            [Authorize(AuthenticationSchemes = Auth.Shopper)] async (SaveConnectionRequest request, ClaimsPrincipal user, BankConnectionService service) =>
            {
                var c = await service.SaveAsync(Auth.Buyer(user), request.PublicToken);
                return Results.Created($"api/bank/connections/{c.Id}", new
                {
                    connectionId = c.Id,
                    institutionName = c.InstitutionName,
                    type = c.HasBankAccounts && c.HasInvestmentAccounts ? "both" : c.HasBankAccounts ? "bank" : "brokerage",
                    accounts = Buyer.ReadAccounts(c).Select(a => new { id = a.Id, a.Name, a.Mask, a.Type, a.Subtype }),
                });
            }).WithTags("BankEndpoints");

        app.MapDelete("api/bank/connections/{connectionId}",
            [Authorize(AuthenticationSchemes = Auth.Shopper)] async (int connectionId, ClaimsPrincipal user, BankConnectionService service) =>
            {
                await service.DisconnectAsync(Auth.Buyer(user), connectionId);
                return Results.NoContent();
            }).WithTags("BankEndpoints");
    }
}

public class BankPaymentEndpoints : IEndpoint
{
    private static object Present(CheckoutQuote q) => new
    {
        checkoutId = q.CheckoutId,
        subtotal = q.Subtotal,
        shippingCost = q.ShippingCost,
        total = q.Total,
        options = q.Options.Select(o => o.Speed),
        optionDetails = q.Options.Select(o => new
        {
            speed = o.Speed,
            fee = o.Fee,
            total = o.Total,
            arrivesOn = o.ArrivesOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        }),
    };

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment",
            [Authorize(AuthenticationSchemes = Auth.Shopper)] async (StartBankPaymentInput request, ClaimsPrincipal user, BankPaymentService service) =>
                Results.Ok(Present(await service.StartAsync(Auth.Buyer(user), request))))
            .WithTags("BankEndpoints");

        app.MapPut("api/checkout/bank-payment/{checkoutId}/shipping",
            [Authorize(AuthenticationSchemes = Auth.Shopper)] async (Guid checkoutId, ChangeShippingRequest request, ClaimsPrincipal user, BankPaymentService service) =>
                Results.Ok(Present(await service.ChangeShippingAsync(Auth.Buyer(user), checkoutId, request.ShippingCost))))
            .WithTags("BankEndpoints");

        app.MapPost("api/checkout/bank-payment/{checkoutId}/confirm",
            [Authorize(AuthenticationSchemes = Auth.Shopper)] async (Guid checkoutId, ConfirmRequest request, ClaimsPrincipal user, BankPaymentService service) =>
            {
                var c = await service.ConfirmAsync(Auth.Buyer(user), checkoutId, request.Speed);
                return Results.Ok(new
                {
                    orderId = c.OrderId,
                    speed = c.Speed,
                    fee = c.Fee,
                    total = c.Total,
                    arrivesOn = c.ArrivesOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                });
            }).WithTags("BankEndpoints");

        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = Auth.Shopper)] async (int orderId, ClaimsPrincipal user, BankPaymentService service) =>
                Results.Ok(await service.CancelAsync(Auth.Buyer(user), orderId)))
            .WithTags("BankEndpoints");
    }
}

public class SupportEndpoints : IEndpoint
{
    private const string Admin = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/bank-activity",
            [Authorize(Roles = Admin, AuthenticationSchemes = Auth.Shopper)] async (string email, BankActivityService service) =>
            {
                var (transactions, warnings) = await service.GetActivityAsync(email);
                return Results.Ok(new { transactions, warnings });
            }).WithTags("BankEndpoints");

        app.MapGet("api/support/shoppers/{email}/transactions",
            [Authorize(Roles = Admin, AuthenticationSchemes = Auth.Shopper)] async (string email, string? from, string? to, BankActivityService service) =>
            {
                if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ||
                    !DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                {
                    throw new ApiException(400, "from and to are required, formatted YYYY-MM-DD.");
                }
                if (f > t)
                {
                    throw new ApiException(400, "from must not be after to.");
                }
                var (transactions, warnings) = await service.GetTransactionsBetweenAsync(email, f, t);
                return Results.Ok(new { transactions, warnings });
            }).WithTags("BankEndpoints");
    }
}

public class PortfolioEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/portfolio",
            [Authorize(AuthenticationSchemes = Auth.Shopper)] async (ClaimsPrincipal user, PortfolioService service) =>
                Results.Ok(new { positions = await service.GetAsync(Auth.Buyer(user)) }))
            .WithTags("BankEndpoints");
    }
}
