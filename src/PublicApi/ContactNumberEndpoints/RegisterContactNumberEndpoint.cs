using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Auth;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IContactNumberService contactNumbers) =>
            {
                return await HandleAsync(request, user, contactNumbers);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService contactNumbers)
        => HandleAsync(request, new ClaimsPrincipal(), contactNumbers);

    public async Task<IResult> HandleAsync(
        RegisterContactNumberRequest request,
        ClaimsPrincipal user,
        IContactNumberService contactNumbers)
    {
        try
        {
            var created = await contactNumbers.RegisterAsync(HttpUser.GetBuyerId(user), request.PhoneNumber);
            var response = new RegisterContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = created.Id,
                PhoneNumber = created.CanonicalNumber
            };
            return Results.Created($"api/contact-numbers/{created.Id}", response);
        }
        catch (InvalidContactNumberException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}
