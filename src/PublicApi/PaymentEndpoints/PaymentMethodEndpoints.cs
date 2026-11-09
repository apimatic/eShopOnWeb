using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Saves a card or a bank account for the shopper in the JWT
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, ShopperDirectory shoppers, PaymentMethodService service, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopper(user);
                var saved = await service.Save(email, request, ct);
                return Results.Created($"api/payment-methods/{saved.Id}", saved);
            })
            .Produces<PaymentMethodDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>
/// Lists the saved cards and bank accounts of the shopper in the JWT
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ShopperDirectory shoppers, PaymentMethodService service, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopper(user);
                return Results.Ok(await service.List(email, ct));
            })
            .Produces<System.Collections.Generic.List<PaymentMethodDto>>()
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>
/// Removes a saved card or bank account of the shopper in the JWT
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string paymentMethodId, ClaimsPrincipal user, ShopperDirectory shoppers, PaymentMethodService service, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopper(user);
                await service.Remove(email, paymentMethodId, ct);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>
/// Support staff: every card and bank account a shopper ever saved, removed ones included
/// </summary>
public class ShopperPaymentMethodsForSupportEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/shoppers/{email}/payment-methods",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, PaymentMethodService service, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                    throw AppException.BadRequest("The shopper's e-mail address is not valid.");
                return Results.Ok(await service.ForSupport(email, ct));
            })
            .Produces<System.Collections.Generic.List<SupportPaymentMethodDto>>()
            .WithTags("PaymentMethodEndpoints");
    }
}
