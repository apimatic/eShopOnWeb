using System;
using System.Collections.Generic;
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

/// <summary>
/// Lists the caller's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest(), user, paymentService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentService paymentService)
        => throw new NotImplementedException("Use the overload carrying the caller identity.");

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ClaimsPrincipal user, IPaymentService paymentService)
    {
        var buyerId = user.Identity!.Name!;
        var response = new ListPaymentMethodsResponse(request.CorrelationId());

        var savedCards = await paymentService.GetSavedCardsAsync(buyerId);
        response.PaymentMethods = savedCards.Select(SavedPaymentMethodDto.From).ToList();
        return Results.Ok(response);
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }

    public List<SavedPaymentMethodDto> PaymentMethods { get; set; } = new List<SavedPaymentMethodDto>();
}
