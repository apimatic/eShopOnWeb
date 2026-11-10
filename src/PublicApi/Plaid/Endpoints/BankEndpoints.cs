using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Plaid.Data;
using Microsoft.eShopWeb.PublicApi.Plaid.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Plaid.Endpoints;

internal static class EndpointHelpers
{
    public const string Jwt = JwtBearerDefaults.AuthenticationScheme;
    public const string Admins = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    /// <summary>The caller's identity comes from the JWT, never from the request.</summary>
    public static string UserName(ClaimsPrincipal user) =>
        user.Identity?.Name is { Length: > 0 } name ? name : throw new BankApiException(401, "The token carries no user name.");

    public static object Connection(BankConnection c) => new
    {
        id = c.Id,
        connectionId = c.Id,
        institutionId = c.InstitutionId,
        institutionName = c.InstitutionName,
        kind = c.IsBank && c.IsBrokerage ? "bank,brokerage" : c.IsBrokerage ? BankConnectionService.Brokerage : BankConnectionService.Bank,
        isBank = c.IsBank,
        isBrokerage = c.IsBrokerage,
        createdAt = c.CreatedAt,
        accounts = c.Accounts.Select(a => new { id = a.Id, accountId = a.PlaidAccountId, name = a.Name, mask = a.Mask, type = a.Type, subtype = a.Subtype }),
    };

    public static string Date(DateTime d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

public record LinkTokenRequest(string? Kind);

public record SaveConnectionRequest(string? PublicToken, string? Kind);

/// <summary>Feature 1: the bank picker.</summary>
public class BankListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/banks",
            [Authorize(AuthenticationSchemes = EndpointHelpers.Jwt)] async (string? query, BankDirectoryService banks, CancellationToken ct) =>
            {
                var list = await banks.GetBanksAsync(query, ct);
                return Results.Ok(new
                {
                    total = list.Count,
                    banks = list.Select(b => new { id = b.Id, name = b.Name, url = b.Url, primaryColor = b.PrimaryColor, oauth = b.Oauth }),
                });
            })
            .WithTags("BankEndpoints");
    }
}

/// <summary>Features 2 and 3: connect and disconnect.</summary>
public class BankConnectionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/link-token",
            [Authorize(AuthenticationSchemes = EndpointHelpers.Jwt)] async (LinkTokenRequest? body, ClaimsPrincipal user, BankConnectionService connections, CancellationToken ct) =>
            {
                var result = await connections.CreateLinkTokenAsync(EndpointHelpers.UserName(user), body?.Kind, ct);
                return Results.Ok(new { linkToken = result.LinkToken, expiration = result.Expiration, kind = result.Kind });
            })
            .WithTags("BankEndpoints");

        app.MapPost("api/bank/connections",
            [Authorize(AuthenticationSchemes = EndpointHelpers.Jwt)] async (SaveConnectionRequest? body, ClaimsPrincipal user, BankConnectionService connections, CancellationToken ct) =>
            {
                var saved = await connections.SaveAsync(EndpointHelpers.UserName(user), body?.PublicToken, body?.Kind, ct);
                return Results.Created($"/api/bank/connections/{saved.Id}", EndpointHelpers.Connection(saved));
            })
            .WithTags("BankEndpoints");

        app.MapGet("api/bank/connections",
            [Authorize(AuthenticationSchemes = EndpointHelpers.Jwt)] async (ClaimsPrincipal user, BankConnectionService connections, CancellationToken ct) =>
            {
                var list = await connections.ListAsync(EndpointHelpers.UserName(user), ct);
                return Results.Ok(new { connections = list.Select(EndpointHelpers.Connection) });
            })
            .WithTags("BankEndpoints");

        app.MapDelete("api/bank/connections/{connectionId}",
            [Authorize(AuthenticationSchemes = EndpointHelpers.Jwt)] async (Guid connectionId, ClaimsPrincipal user, BankConnectionService connections, CancellationToken ct) =>
            {
                await connections.DisconnectAsync(EndpointHelpers.UserName(user), connectionId, ct);
                return Results.NoContent();
            })
            .WithTags("BankEndpoints");
    }
}

public record StartBankPaymentRequest(Guid? ConnectionId, string? AccountId, BankPaymentItemRequest[]? Items, decimal? ShippingCost, ShipToRequest? ShipToAddress);

public record ChangeShippingRequest(decimal? ShippingCost);

public record ConfirmBankPaymentRequest(string? Speed);

/// <summary>Features 4 to 7: pay, change the shipping, confirm, cancel.</summary>
public class BankPaymentEndpoints : IEndpoint
{
    private static object View(CheckoutView v) => new
    {
        checkoutId = v.Payment.Id,
        id = v.Payment.Id,
        connectionId = v.Payment.ConnectionId,
        itemsTotal = v.Payment.ItemsTotal,
        shippingCost = v.Payment.ShippingCost,
        total = v.Total,
        options = v.Options.Select(o => o.Speed),
        optionDetails = v.Options.Select(o => new { speed = o.Speed, fee = o.Fee, total = o.Total, arrivesOn = EndpointHelpers.Date(o.ArrivesOn) }),
    };

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment",
            [Authorize(AuthenticationSchemes = EndpointHelpers.Jwt)] async (StartBankPaymentRequest? body, ClaimsPrincipal user, BankPaymentService payments, CancellationToken ct) =>
            {
                if (body is null) throw BankApiException.BadRequest("A request body is required.");
                var view = await payments.StartAsync(EndpointHelpers.UserName(user), body.ConnectionId, body.AccountId, body.Items, body.ShippingCost, body.ShipToAddress, ct);
                return Results.Created($"/api/checkout/bank-payment/{view.Payment.Id}", View(view));
            })
            .WithTags("BankPaymentEndpoints");

        app.MapGet("api/checkout/bank-payment/{checkoutId}",
            [Authorize(AuthenticationSchemes = EndpointHelpers.Jwt)] async (Guid checkoutId, ClaimsPrincipal user, BankPaymentService payments, CancellationToken ct) =>
                Results.Ok(View(await payments.GetAsync(EndpointHelpers.UserName(user), checkoutId, ct))))
            .WithTags("BankPaymentEndpoints");

        app.MapPut("api/checkout/bank-payment/{checkoutId}/shipping",
            [Authorize(AuthenticationSchemes = EndpointHelpers.Jwt)] async (Guid checkoutId, ChangeShippingRequest? body, ClaimsPrincipal user, BankPaymentService payments, CancellationToken ct) =>
                Results.Ok(View(await payments.ChangeShippingAsync(EndpointHelpers.UserName(user), checkoutId, body?.ShippingCost, ct))))
            .WithTags("BankPaymentEndpoints");

        app.MapPost("api/checkout/bank-payment/{checkoutId}/confirm",
            [Authorize(AuthenticationSchemes = EndpointHelpers.Jwt)] async (Guid checkoutId, ConfirmBankPaymentRequest? body, ClaimsPrincipal user, BankPaymentService payments, CancellationToken ct) =>
            {
                var c = await payments.ConfirmAsync(EndpointHelpers.UserName(user), checkoutId, body?.Speed, ct);
                var p = c.Payment;
                return Results.Ok(new
                {
                    orderId = p.OrderId,
                    checkoutId = p.Id,
                    speed = p.Speed,
                    total = c.Total,
                    fee = p.Fee,
                    arrivesOn = EndpointHelpers.Date(p.ArrivesOn!.Value),
                });
            })
            .WithTags("BankPaymentEndpoints");

        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = EndpointHelpers.Jwt)] async (int orderId, ClaimsPrincipal user, BankPaymentService payments, CancellationToken ct) =>
            {
                var p = await payments.CancelOrderAsync(EndpointHelpers.UserName(user), orderId, ct);
                return Results.Ok(new
                {
                    orderId = p.OrderId,
                    status = p.Status,
                    refunded = p.ItemsTotal + p.ShippingCost + p.Fee,
                });
            })
            .WithTags("BankPaymentEndpoints");
    }
}

/// <summary>Features 8 and 9: what support staff can see.</summary>
public class SupportBankEndpoints : IEndpoint
{
    private static object Lines(ActivityResult r) => new
    {
        transactions = r.Transactions.Select(t => new
        {
            name = t.Name,
            amount = t.Amount,
            date = EndpointHelpers.Date(t.Date),
            pending = t.Pending,
            connectionId = t.ConnectionId,
            institutionName = t.InstitutionName,
            accountId = t.AccountId,
        }),
        problems = r.Problems.Select(p => new { connectionId = p.ConnectionId, institutionName = p.InstitutionName, reason = p.Reason }),
    };

    private static DateTime ParseDate(string? text, string name)
    {
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
            throw BankApiException.BadRequest($"{name} is required, as YYYY-MM-DD.");
        return date;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/bank-activity",
            [Authorize(Roles = EndpointHelpers.Admins, AuthenticationSchemes = EndpointHelpers.Jwt)] async (string email, BankActivityService activity, CancellationToken ct) =>
                Results.Ok(Lines(await activity.GetActivityAsync(email, ct))))
            .WithTags("SupportBankEndpoints");

        app.MapGet("api/support/shoppers/{email}/transactions",
            [Authorize(Roles = EndpointHelpers.Admins, AuthenticationSchemes = EndpointHelpers.Jwt)] async (string email, string? from, string? to, BankActivityService activity, CancellationToken ct) =>
            {
                var start = ParseDate(from, "from");
                var end = ParseDate(to, "to");
                if (start > end) throw BankApiException.BadRequest("from must not be after to.");
                return Results.Ok(Lines(await activity.GetTransactionsBetweenAsync(email, start, end, ct)));
            })
            .WithTags("SupportBankEndpoints");
    }
}

/// <summary>Feature 10: the shopper's holdings.</summary>
public class PortfolioEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/portfolio",
            [Authorize(AuthenticationSchemes = EndpointHelpers.Jwt)] async (ClaimsPrincipal user, PortfolioService portfolio, CancellationToken ct) =>
            {
                var positions = await portfolio.GetAsync(EndpointHelpers.UserName(user), ct);
                return Results.Ok(new
                {
                    positions = positions.Select(p =>
                    {
                        // An option is counted in contracts, anything else in quantity; the other one is left out.
                        var position = new System.Collections.Generic.Dictionary<string, object?>
                        {
                            ["ticker"] = p.Ticker,
                            ["name"] = p.Name,
                            ["value"] = p.Value,
                        };
                        if (p.Contracts is not null) position["contracts"] = p.Contracts;
                        else position["quantity"] = p.Quantity;
                        position["connectionId"] = p.ConnectionId;
                        position["institutionName"] = p.InstitutionName;
                        position["accountId"] = p.AccountId;
                        return position;
                    }),
                });
            })
            .WithTags("PortfolioEndpoints");
    }
}
