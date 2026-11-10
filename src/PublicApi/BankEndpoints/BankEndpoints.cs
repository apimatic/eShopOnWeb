using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Plaid;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

internal static class Auth
{
    public const string Scheme = JwtBearerDefaults.AuthenticationScheme;
    public const string Admins = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public static IResult Unauthenticated() => ApiResults.Error(StatusCodes.Status401Unauthorized, "The token carries no identity.");
}

/// <summary>Lists / searches the banks Plaid supports (the checkout bank picker).</summary>
public class BankListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/banks",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] (string? query, BankPickerService picker, CancellationToken ct) =>
                picker.ListAsync(query, ct))
            .WithTags("BankEndpoints");
    }
}

/// <summary>Creates the Plaid Link token the browser needs to open Link.</summary>
public class CreateLinkTokenEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/link-token",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] (ClaimsPrincipal user, ConnectionService connections, CancellationToken ct, LinkTokenRequest? request = null) =>
                user.UserName() is { } name ? connections.CreateLinkTokenAsync(name, request, ct) : System.Threading.Tasks.Task.FromResult(Auth.Unauthenticated()))
            .WithTags("BankEndpoints");
    }
}

/// <summary>Saves a connection from what Link hands the browser.</summary>
public class SaveConnectionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/connections",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] (SaveConnectionRequest request, ClaimsPrincipal user, ConnectionService connections, CancellationToken ct) =>
                user.UserName() is { } name ? connections.SaveAsync(name, request, ct) : System.Threading.Tasks.Task.FromResult(Auth.Unauthenticated()))
            .WithTags("BankEndpoints");
    }
}

/// <summary>Lists the caller's connections.</summary>
public class ListConnectionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/bank/connections",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] (ClaimsPrincipal user, ConnectionService connections, CancellationToken ct) =>
                user.UserName() is { } name ? connections.ListAsync(name, ct) : System.Threading.Tasks.Task.FromResult(Auth.Unauthenticated()))
            .WithTags("BankEndpoints");
    }
}

/// <summary>Disconnects a bank or brokerage account.</summary>
public class DisconnectEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/bank/connections/{connectionId}",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] (string connectionId, ClaimsPrincipal user, ConnectionService connections, CancellationToken ct) =>
                user.UserName() is { } name ? connections.DisconnectAsync(name, connectionId, ct) : System.Threading.Tasks.Task.FromResult(Auth.Unauthenticated()))
            .WithTags("BankEndpoints");
    }
}

/// <summary>Starts a bank payment at checkout.</summary>
public class StartBankPaymentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] (StartBankPaymentRequest request, ClaimsPrincipal user, CheckoutService checkout, CancellationToken ct) =>
                user.UserName() is { } name ? checkout.StartAsync(name, request, ct) : System.Threading.Tasks.Task.FromResult(Auth.Unauthenticated()))
            .WithTags("CheckoutEndpoints");
    }
}

/// <summary>Changes the shipping cost of a started bank payment.</summary>
public class ChangeShippingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/checkout/bank-payment/{checkoutId}/shipping",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] (string checkoutId, ChangeShippingRequest request, ClaimsPrincipal user, CheckoutService checkout, CancellationToken ct) =>
                user.UserName() is { } name ? checkout.ChangeShippingAsync(name, checkoutId, request, ct) : System.Threading.Tasks.Task.FromResult(Auth.Unauthenticated()))
            .WithTags("CheckoutEndpoints");
    }
}

/// <summary>Confirms a bank payment with a speed and charges it.</summary>
public class ConfirmBankPaymentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment/{checkoutId}/confirm",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] (string checkoutId, ConfirmBankPaymentRequest request, ClaimsPrincipal user, CheckoutService checkout, CancellationToken ct) =>
                user.UserName() is { } name ? checkout.ConfirmAsync(name, checkoutId, request, ct) : System.Threading.Tasks.Task.FromResult(Auth.Unauthenticated()))
            .WithTags("CheckoutEndpoints");
    }
}

/// <summary>Cancels an order paid by bank, with a full refund.</summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] (int orderId, ClaimsPrincipal user, BankOrderService orders, CancellationToken ct) =>
                user.UserName() is { } name ? orders.CancelAsync(name, orderId, ct) : System.Threading.Tasks.Task.FromResult(Auth.Unauthenticated()))
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Marks an order as shipped; after this it can no longer be cancelled.</summary>
public class ShipOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/ship",
            [Authorize(Roles = Auth.Admins, AuthenticationSchemes = Auth.Scheme)] (int orderId, BankOrderService orders, CancellationToken ct) =>
                orders.ShipAsync(orderId, ct))
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Support: the shop's copy of a shopper's bank activity.</summary>
public class BankActivityEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/bank-activity",
            [Authorize(Roles = Auth.Admins, AuthenticationSchemes = Auth.Scheme)] (string email, BankActivityService activity, CancellationToken ct) =>
                activity.ActivityAsync(email, ct))
            .WithTags("SupportEndpoints");
    }
}

/// <summary>Support: transactions between two dates, straight from the bank.</summary>
public class BankTransactionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/transactions",
            [Authorize(Roles = Auth.Admins, AuthenticationSchemes = Auth.Scheme)] (string email, string? from, string? to, string? connectionId, BankActivityService activity, CancellationToken ct) =>
                activity.BetweenAsync(email, from, to, connectionId, ct))
            .WithTags("SupportEndpoints");
    }
}

/// <summary>The shopper's brokerage holdings.</summary>
public class PortfolioEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/portfolio",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] (ClaimsPrincipal user, PortfolioService portfolio, CancellationToken ct) =>
                user.UserName() is { } name ? portfolio.GetAsync(name, ct) : System.Threading.Tasks.Task.FromResult(Auth.Unauthenticated()))
            .WithTags("PortfolioEndpoints");
    }
}
