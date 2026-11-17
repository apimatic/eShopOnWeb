using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest>
{
    private readonly IPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SavePaymentMethodEndpoint(IPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (SavePaymentMethodRequest request) =>
            {
                return await HandleAsync(request);
            })
            .Produces<SavePaymentMethodResponse>(201)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request)
    {
        var ownerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        var card = await _paymentService.SaveCardAsync(ownerId, new SaveCardInput(request.ResolveCard()));

        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = card.PaymentMethodId,
            PaymentMethod = PaymentDtoMapper.ToDto(card)
        };

        return Results.Created($"api/payment-methods/{card.PaymentMethodId}", response);
    }
}

/// <summary>
/// Lists the caller's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest>
{
    private readonly IPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListPaymentMethodsEndpoint(IPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest());
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request)
    {
        var ownerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        var cards = await _paymentService.ListCardsAsync(ownerId);

        var response = new ListPaymentMethodsResponse(request.CorrelationId());
        response.PaymentMethods.AddRange(cards.Select(PaymentDtoMapper.ToDto));
        return Results.Ok(response);
    }
}

/// <summary>
/// Removes one of the caller's saved cards.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest>
{
    private readonly IPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeletePaymentMethodEndpoint(IPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int paymentMethodId) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest(paymentMethodId));
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request)
    {
        var ownerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        var card = await _paymentService.DeleteCardAsync(ownerId, request.PaymentMethodId);

        var response = new DeletePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = card.PaymentMethodId,
            Removed = true
        };

        return Results.Ok(response);
    }
}
