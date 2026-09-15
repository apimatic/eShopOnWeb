using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here; what is stored is the provider's canonical form.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IContactNumberService service) =>
            {
                request.SetCaller(user);
                return await HandleAsync(request, service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces<RegisterContactNumberRejectedResponse>(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new RegisterContactNumberRejectedResponse(request.CorrelationId())
            {
                Message = "A phone number is required.",
                ValidationErrors = new[] { "PHONE_NUMBER_REQUIRED" }
            });
        }

        var result = await service.RegisterAsync(request.CallerUserName, request.PhoneNumber);
        if (!result.Succeeded || result.ContactNumber is null)
        {
            return Results.BadRequest(new RegisterContactNumberRejectedResponse(request.CorrelationId())
            {
                ValidationErrors = result.ValidationErrors
            });
        }

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = result.ContactNumber.Id,
            PhoneNumber = result.ContactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}
