using System;
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

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateContactNumberRequest request, HttpContext httpContext, IContactNumberService contactNumbers) =>
            {
                var buyerId = httpContext.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, buyerId, contactNumbers);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumbers) =>
        throw new NotSupportedException();

    private async Task<IResult> HandleAsync(CreateContactNumberRequest request, string buyerId, IContactNumberService contactNumbers)
    {
        try
        {
            var created = await contactNumbers.RegisterAsync(buyerId, request.PhoneNumber);
            var response = new CreateContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = created.Id,
                CanonicalNumber = created.CanonicalNumber
            };
            return Results.Created($"api/contact-numbers/{created.Id}", response);
        }
        catch (InvalidContactNumberException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (DuplicateException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }
}
