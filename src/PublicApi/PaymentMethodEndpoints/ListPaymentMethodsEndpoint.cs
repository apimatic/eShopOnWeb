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
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>The signed-in shopper's saved cards. GET /api/payment-methods</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentMethodService service, ClaimsPrincipal user) =>
            {
                var response = new ListPaymentMethodsResponse();
                var methods = await service.ListAsync(user.GetBuyerId());
                response.PaymentMethods = methods.Select(SavedPaymentMethodDto.FromEntity).ToList();
                return Results.Ok(response);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(IPaymentMethodService service) => Task.FromResult(Results.Empty as IResult);
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse() { }
    public List<SavedPaymentMethodDto> PaymentMethods { get; set; } = new();
}
