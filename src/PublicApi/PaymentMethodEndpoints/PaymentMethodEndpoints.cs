using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ISavedPaymentMethodService methods, ClaimsPrincipal user) =>
            {
                request.BuyerId = ApiCaller.BuyerId(user);
                return await HandleAsync(request, methods);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService methods)
    {
        if (request.Card == null)
        {
            return Results.BadRequest(new { Message = "Card details are required." });
        }

        var saved = await methods.SaveAsync(request.BuyerId, ApiCaller.ToCard(request.Card), default);
        var dto = ApiCaller.ToDto(saved);
        return Results.Created($"api/payment-methods/{saved.Id}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = dto
        });
    }
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedPaymentMethodService methods) =>
            {
                return await HandleAsync(user, methods);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISavedPaymentMethodService methods)
    {
        var list = await methods.ListAsync(ApiCaller.BuyerId(user), default);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = list.Select(ApiCaller.ToDto).ToList()
        });
    }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedPaymentMethodService methods, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest
                {
                    PaymentMethodId = paymentMethodId,
                    BuyerId = ApiCaller.BuyerId(user)
                }, methods);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedPaymentMethodService methods)
    {
        await methods.DeleteAsync(request.BuyerId, request.PaymentMethodId, default);
        return Results.NoContent();
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public CardDetailsRequest? Card { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public System.Collections.Generic.List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}
