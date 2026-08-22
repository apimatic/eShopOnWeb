using System.Collections.Generic;
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
            async (SavePaymentMethodRequest request, ISavedPaymentMethodService paymentMethods, ClaimsPrincipal user) =>
            {
                var saved = await paymentMethods.SaveCardAsync(
                    OrderEndpointHelpers.GetBuyerId(user),
                    CardRequestMapping.ToPaymentSource(request.Card));
                return Results.Created($"api/payment-methods/{saved.Id}", new SavePaymentMethodResponse
                {
                    PaymentMethodId = saved.Id,
                    PaymentMethod = ToDto(saved)
                });
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CardRequest request, ISavedPaymentMethodService paymentMethods) =>
        Task.FromResult(Results.BadRequest());

    internal static PaymentMethodDto ToDto(SavedPaymentMethod saved) => new()
    {
        PaymentMethodId = saved.Id,
        Brand = saved.Brand,
        Last4 = saved.Last4,
        Expiry = saved.Expiry,
        CardholderName = saved.CardholderName
    };
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, string, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISavedPaymentMethodService paymentMethods, ClaimsPrincipal user) =>
            {
                var saved = await paymentMethods.ListAsync(OrderEndpointHelpers.GetBuyerId(user));
                return Results.Ok(new ListPaymentMethodsResponse
                {
                    PaymentMethods = saved.Select(CreatePaymentMethodEndpoint.ToDto).ToList()
                });
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(string request, ISavedPaymentMethodService paymentMethods) =>
        Task.FromResult(Results.BadRequest());
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ISavedPaymentMethodService paymentMethods, ClaimsPrincipal user) =>
            {
                await paymentMethods.DeleteAsync(OrderEndpointHelpers.GetBuyerId(user), paymentMethodId);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(int request, ISavedPaymentMethodService paymentMethods) =>
        Task.FromResult(Results.BadRequest());
}

public class SavePaymentMethodRequest : BaseRequest
{
    public CardRequest Card { get; set; } = new();
}

public class SavePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto? PaymentMethod { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
}
