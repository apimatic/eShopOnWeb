using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using Admins = BlazorShared.Authorization.Constants.Roles;

namespace Microsoft.eShopWeb.PublicApi.Banking.Endpoints;

// Errors thrown as BankApiException / PlaidException are turned into a 4xx (or 502) with a reason by ExceptionMiddleware.

/// <summary>
/// Bank picker: the banks Plaid supports, optionally searched by name.
/// </summary>
public class ListBanksEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/banks",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? query, HttpContext context, BankDirectoryService banks, CancellationToken ct) =>
            {
                var listing = await banks.GetAsync(query, ct);
                // false while the full list is still loading in the background after startup
                context.Response.Headers["X-Bank-List-Complete"] = listing.Complete ? "true" : "false";
                return Results.Ok(listing.Banks);
            })
            .Produces<BankSummary[]>()
            .WithTags("BankEndpoints");
    }
}

/// <summary>
/// Creates what the browser needs to open Plaid Link.
/// </summary>
public class CreateLinkTokenEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/link-token",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LinkTokenRequest? request,
                BankConnectionService connections, CancellationToken ct) =>
                Results.Ok(await connections.CreateLinkTokenAsync(Shopper.Require(user), request?.Type, ct)))
            .Produces<LinkTokenResponse>()
            .WithTags("BankEndpoints");
    }
}

/// <summary>
/// Saves a connection from the public token Link hands the browser.
/// </summary>
public class SaveConnectionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/connections",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, SaveConnectionRequest request, BankConnectionService connections, CancellationToken ct) =>
            {
                var saved = await connections.SaveAsync(Shopper.Require(user), request, ct);
                return Results.Created($"api/bank/connections/{saved.ConnectionId}", saved);
            })
            .Produces<ConnectionResponse>(StatusCodes.Status201Created)
            .WithTags("BankEndpoints");
    }
}

/// <summary>
/// Lists the caller's connected banks and brokerage accounts.
/// </summary>
public class ListConnectionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/bank/connections",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, BankConnectionService connections, CancellationToken ct) =>
                Results.Ok(await connections.ListAsync(Shopper.Require(user), ct)))
            .Produces<ConnectionResponse[]>()
            .WithTags("BankEndpoints");
    }
}

/// <summary>
/// Disconnects a bank or brokerage account (and stops paying Plaid for it).
/// </summary>
public class DisconnectEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/bank/connections/{connectionId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string connectionId, ClaimsPrincipal user, BankConnectionService connections, CancellationToken ct) =>
            {
                await connections.DisconnectAsync(Shopper.Require(user), connectionId, ct);
                return Results.NoContent();
            })
            .WithTags("BankEndpoints");
    }
}

/// <summary>
/// Starts a bank payment at checkout.
/// </summary>
public class StartBankPaymentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, StartBankPaymentRequest request, BankPaymentService payments, CancellationToken ct) =>
                Results.Ok(await payments.StartAsync(Shopper.Require(user), request, ct)))
            .Produces<BankPaymentQuote>()
            .WithTags("CheckoutEndpoints");
    }
}

/// <summary>
/// Changes the shipping cost of a started bank payment.
/// </summary>
public class ChangeShippingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/checkout/bank-payment/{checkoutId}/shipping",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string checkoutId, ClaimsPrincipal user, ChangeShippingRequest request, BankPaymentService payments, CancellationToken ct) =>
                Results.Ok(await payments.ChangeShippingAsync(Shopper.Require(user), checkoutId, request, ct)))
            .Produces<BankPaymentQuote>()
            .WithTags("CheckoutEndpoints");
    }
}

/// <summary>
/// Confirms a bank payment with one of the offered speeds and charges the shopper.
/// </summary>
public class ConfirmBankPaymentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment/{checkoutId}/confirm",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string checkoutId, ClaimsPrincipal user, ConfirmBankPaymentRequest request, BankPaymentService payments, CancellationToken ct) =>
                Results.Ok(await payments.ConfirmAsync(Shopper.Require(user), checkoutId, request, ct)))
            .Produces<OrderConfirmation>()
            .WithTags("CheckoutEndpoints");
    }
}

/// <summary>
/// Cancels an order paid by bank before it ships and returns all the money.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, BankPaymentService payments, CancellationToken ct) =>
                Results.Ok(await payments.CancelAsync(Shopper.Require(user), orderId, ct)))
            .Produces<CancelOrderResponse>()
            .WithTags("CheckoutEndpoints");
    }
}

/// <summary>
/// Support: the shop's synced copy of the activity on a shopper's connected banks (amounts: positive = money out).
/// </summary>
public class BankActivityEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/bank-activity",
            [Authorize(Roles = Admins.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, BankActivityService activity, CancellationToken ct) =>
                Results.Ok(await activity.GetActivityAsync(RequireShopper(email), ct)))
            .Produces<BankActivityResponse>()
            .WithTags("SupportEndpoints");
    }

    internal static string RequireShopper(string email) =>
        Shopper.Normalize(email) ?? throw BankApiException.BadRequest("A shopper email is required.");
}

/// <summary>
/// Support: live transactions between two dates on all of a shopper's connected banks (amounts: positive = money out).
/// </summary>
public class BankTransactionsLookupEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/transactions",
            [Authorize(Roles = Admins.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, string? from, string? to, BankActivityService activity, CancellationToken ct) =>
                Results.Ok(await activity.LookupAsync(BankActivityEndpoint.RequireShopper(email), from, to, ct)))
            .Produces<TransactionsLookupResponse>()
            .WithTags("SupportEndpoints");
    }
}

/// <summary>
/// The caller's holdings across connected brokerage accounts.
/// </summary>
public class PortfolioEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/portfolio",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, PortfolioService portfolio, CancellationToken ct) =>
                Results.Ok(await portfolio.GetAsync(Shopper.Require(user), ct)))
            .Produces<PortfolioResponse>()
            .WithTags("PortfolioEndpoints");
    }
}
