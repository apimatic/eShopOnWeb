using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Saves a card or bank account for the signed-in shopper
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, ShopperService shoppers) =>
            {
                var email = await shoppers.ResolveEmailAsync(user);
                var created = await shoppers.SaveMethodAsync(email, request);
                return Results.Created($"api/payment-methods/{created.Id}", created);
            })
            .Produces<PaymentMethodDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>
/// Lists the signed-in shopper's saved cards and bank accounts
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ShopperService shoppers) =>
            {
                var email = await shoppers.ResolveEmailAsync(user);
                return Results.Ok(new ListPaymentMethodsResponse { PaymentMethods = await shoppers.ListMethodsAsync(email) });
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>
/// Removes one of the signed-in shopper's saved payment methods
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string paymentMethodId, ClaimsPrincipal user, ShopperService shoppers) =>
            {
                var email = await shoppers.ResolveEmailAsync(user);
                await shoppers.RemoveMethodAsync(email, paymentMethodId);
                return Results.NoContent();
            })
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>
/// Support view: every payment method a shopper has saved, removed ones included
/// </summary>
public class AdminShopperPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/shoppers/{email}/payment-methods",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, ShopperService shoppers) => Results.Ok(await shoppers.SupportListAsync(email)))
            .Produces<SupportPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>
/// Closes the signed-in shopper's account and erases their data (at Stax too)
/// </summary>
public class CloseAccountEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/account/close",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ShopperService shoppers) =>
            {
                var email = await shoppers.ResolveEmailAsync(user);
                await shoppers.CloseAccountAsync(email);
                return Results.NoContent();
            })
            .WithTags("AccountEndpoints");
    }
}
