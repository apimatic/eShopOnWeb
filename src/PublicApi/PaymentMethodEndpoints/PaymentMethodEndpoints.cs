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

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest>
{
    private readonly ISavedPaymentMethodService _savedCards;

    public CreatePaymentMethodEndpoint(ISavedPaymentMethodService savedCards)
    {
        _savedCards = savedCards;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request) => Task.FromResult(Results.BadRequest());

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, HttpContext httpContext)
    {
        var buyerId = PaymentRequestMapper.RequireBuyerId(httpContext);
        var card = PaymentRequestMapper.ToCardDetails(request.Card ?? new CardPaymentRequest());
        var saved = await _savedCards.SaveAsync(buyerId, card, httpContext.RequestAborted);
        var dto = saved.ToDto();
        return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = dto.PaymentMethodId,
            PaymentMethod = dto
        });
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardPaymentRequest? Card { get; set; }
}

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly ISavedPaymentMethodService _savedCards;

    public ListPaymentMethodsEndpoint(ISavedPaymentMethodService savedCards)
    {
        _savedCards = savedCards;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var buyerId = PaymentRequestMapper.RequireBuyerId(httpContext);
        var methods = await _savedCards.ListAsync(buyerId, httpContext.RequestAborted);
        return Results.Ok(new ListPaymentMethodsResponse
        {
            PaymentMethods = methods.Select(m => m.ToDto()).ToList()
        });
    }
}

public class ListPaymentMethodsResponse
{
    public System.Collections.Generic.List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int>
{
    private readonly ISavedPaymentMethodService _savedCards;

    public DeletePaymentMethodEndpoint(ISavedPaymentMethodService savedCards)
    {
        _savedCards = savedCards;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, HttpContext httpContext) =>
            {
                return await HandleAsync(paymentMethodId, httpContext);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(int paymentMethodId) => Task.FromResult(Results.BadRequest());

    public async Task<IResult> HandleAsync(int paymentMethodId, HttpContext httpContext)
    {
        var buyerId = PaymentRequestMapper.RequireBuyerId(httpContext);
        await _savedCards.DeleteAsync(buyerId, paymentMethodId, httpContext.RequestAborted);
        return Results.NoContent();
    }
}
