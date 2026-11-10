using System;
using System.Globalization;
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

public record LinkTokenRequest(string? Kind);

public record SaveConnectionRequest(string? PublicToken, string? Kind, string? AccountId);

/// <summary>
/// The bank picker: the banks Plaid supports, searchable by name.
/// </summary>
public class BankListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/banks",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                    (string? query, int? count, int? offset, InstitutionService institutions) =>
                    Results.Ok(await institutions.ListAsync(query, count, offset)))
            .Produces<BankListDto>()
            .WithTags("BankEndpoints");
    }
}

/// <summary>
/// Connecting and disconnecting a bank or brokerage account through Plaid Link.
/// </summary>
public class BankConnectionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/link-token",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                    (ClaimsPrincipal user, LinkTokenRequest? request, BankConnectionService connections) =>
                    Results.Ok(await connections.CreateLinkTokenAsync(CurrentUser.Name(user), request?.Kind)))
            .Produces<LinkTokenDto>()
            .WithTags("BankEndpoints");

        app.MapPost("api/bank/connections",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                    (ClaimsPrincipal user, SaveConnectionRequest request,
                        BankConnectionService connections, CancellationToken cancellationToken) =>
                    Results.Ok(await connections.ConnectAsync(CurrentUser.Name(user), request.PublicToken, request.Kind,
                        request.AccountId, cancellationToken)))
            .Produces<ConnectionDto>()
            .WithTags("BankEndpoints");

        app.MapDelete("api/bank/connections/{connectionId}",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                    (ClaimsPrincipal user, int connectionId, BankConnectionService connections,
                        CancellationToken cancellationToken) =>
                {
                    await connections.DisconnectAsync(CurrentUser.Name(user), connectionId, cancellationToken);
                    return Results.NoContent();
                })
            .WithTags("BankEndpoints");
    }
}

/// <summary>
/// Paying for an order by bank: start, change the shipping, confirm, and cancel the order.
/// </summary>
public class BankPaymentEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                    (ClaimsPrincipal user, StartBankPaymentRequest request,
                        BankPaymentService payments, CancellationToken cancellationToken) =>
                    Results.Ok(await payments.StartAsync(CurrentUser.Name(user), request, cancellationToken)))
            .Produces<BankPaymentQuoteDto>()
            .WithTags("CheckoutEndpoints");

        app.MapPut("api/checkout/bank-payment/{checkoutId}/shipping",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                    (ClaimsPrincipal user, Guid checkoutId, ChangeShippingRequest request,
                        BankPaymentService payments, CancellationToken cancellationToken) =>
                    Results.Ok(await payments.ChangeShippingAsync(CurrentUser.Name(user), checkoutId, request, cancellationToken)))
            .Produces<BankPaymentQuoteDto>()
            .WithTags("CheckoutEndpoints");

        app.MapPost("api/checkout/bank-payment/{checkoutId}/confirm",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                    (ClaimsPrincipal user, Guid checkoutId, ConfirmBankPaymentRequest request,
                        BankPaymentService payments, CancellationToken cancellationToken) =>
                    Results.Ok(await payments.ConfirmAsync(CurrentUser.Name(user), checkoutId, request, cancellationToken)))
            .Produces<OrderConfirmationDto>()
            .WithTags("CheckoutEndpoints");

        app.MapPost("api/orders/{orderId}/cancel",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                    (ClaimsPrincipal user, int orderId, BankPaymentService payments,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await payments.CancelOrderAsync(CurrentUser.Name(user), orderId, cancellationToken)))
            .Produces<OrderCancellationDto>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>
/// What support staff (administrators) can see of a shopper's connected banks.
/// </summary>
public class SupportBankEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/bank-activity",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                    (string email, BankActivityService activity, CancellationToken cancellationToken) =>
                    Results.Ok(await activity.GetActivityAsync(email, cancellationToken)))
            .Produces<BankActivityDto>()
            .WithTags("SupportEndpoints");

        app.MapGet("api/support/shoppers/{email}/transactions",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                    (string email, string? from, string? to, BankActivityService activity, CancellationToken cancellationToken) =>
                {
                    var start = ParseDate(from, "from");
                    var end = ParseDate(to, "to");
                    if (end < start)
                        throw ApiProblemException.BadRequest("The 'to' date must not be before the 'from' date.");
                    return Results.Ok(await activity.GetTransactionsBetweenAsync(email, start, end, cancellationToken));
                })
            .Produces<BankActivityDto>()
            .WithTags("SupportEndpoints");
    }

    private static DateOnly ParseDate(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw ApiProblemException.BadRequest($"The '{name}' date is required, as YYYY-MM-DD.");
        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw ApiProblemException.BadRequest($"The '{name}' date must be formatted as YYYY-MM-DD.");
        return date;
    }
}

/// <summary>
/// A shopper's holdings at the brokerage accounts they connected.
/// </summary>
public class PortfolioEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/portfolio",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                    (ClaimsPrincipal user, PortfolioService portfolio, CancellationToken cancellationToken) =>
                    Results.Ok(await portfolio.GetAsync(CurrentUser.Name(user), cancellationToken)))
            .Produces<PortfolioDto>()
            .WithTags("PortfolioEndpoints");
    }
}
