using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext httpContext, IContactNumberService service) =>
            {
                var unauthorized = httpContext.UnauthorizedIfAnonymous();
                if (unauthorized is not null) return unauthorized;
                return await HandleAsync(request, service, httpContext.GetBuyerId()!);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service) =>
        HandleAsync(request, service, string.Empty);

    private async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service, string buyerId)
    {
        var contact = await service.RegisterAsync(buyerId, request.PhoneNumber, default);
        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contact.Id,
            Number = contact.Number
        };
        return Results.Created($"api/contact-numbers/{contact.Id}", response);
    }
}
