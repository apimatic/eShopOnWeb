using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (RegisterContactNumberRequest request, HttpContext httpContext, IContactNumberService service) =>
            {
                var unauthorized = BuyerIdentity.RequireBuyer(httpContext.User, out var buyerId);
                if (unauthorized is not null)
                {
                    return unauthorized;
                }

                return await HandleAsync(new RegisterContactNumberRequest { PhoneNumber = request.PhoneNumber, BuyerId = buyerId }, service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        try
        {
            var contact = await service.RegisterAsync(request.BuyerId, request.PhoneNumber);
            var response = new RegisterContactNumberResponse
            {
                ContactNumberId = contact.Id,
                PhoneNumber = contact.CanonicalNumber
            };
            return Results.Created($"api/contact-numbers/{contact.Id}", response);
        }
        catch (InvalidContactNumberException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}
