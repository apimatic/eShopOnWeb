using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CardPaymentApiRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CardPaymentApiRequest request, HttpContext http, ISavedPaymentMethodService service) =>
                await HandleAsync(request, http, service))
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CardPaymentApiRequest request, ISavedPaymentMethodService service) =>
        HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(CardPaymentApiRequest request, HttpContext http, ISavedPaymentMethodService service)
    {
        var saved = await service.SaveCardAsync(http.RequireBuyerId(), request.ToSource());
        var response = new SavePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            LastDigits = saved.LastDigits,
            Brand = saved.Brand,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, HttpContext, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, ISavedPaymentMethodService service) => await HandleAsync(http, service))
            .Produces<PaymentMethodListResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, ISavedPaymentMethodService service)
    {
        var methods = await service.ListAsync(http.RequireBuyerId());
        return Results.Ok(new PaymentMethodListResponse
        {
            PaymentMethods = methods.Select(PaymentMethodResponse.From).ToList()
        });
    }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, HttpContext http, ISavedPaymentMethodService service) =>
            {
                await service.DeleteAsync(http.RequireBuyerId(), paymentMethodId);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(int paymentMethodId, ISavedPaymentMethodService service) =>
        Task.FromResult(Results.NoContent());
}
