using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CardRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CardRequest request, ISavedPaymentMethodService service, ClaimsPrincipal user) =>
            {
                var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
                var method = await service.SaveAsync(buyerId, PaymentEndpointHelpers.ToCard(request));
                return Results.Created($"api/payment-methods/{method.Id}", ToResponse(method));
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CardRequest request, ISavedPaymentMethodService service)
        => Task.FromResult(Results.BadRequest());

    internal static PaymentMethodResponse ToResponse(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        Last4 = method.Last4,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName,
        CreatedAt = method.CreatedAt
    };
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISavedPaymentMethodService service, ClaimsPrincipal user) => await HandleAsync(user, service))
            .Produces<PaymentMethodResponse[]>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISavedPaymentMethodService service)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
        var methods = await service.ListAsync(buyerId);
        return Results.Ok(methods.Select(CreatePaymentMethodEndpoint.ToResponse).ToList());
    }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ISavedPaymentMethodService service, ClaimsPrincipal user) =>
            {
                var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
                await service.DeleteAsync(buyerId, paymentMethodId);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(int paymentMethodId, ISavedPaymentMethodService service)
        => Task.FromResult(Results.BadRequest());
}
