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
            async (CreateContactNumberRequest request, HttpContext httpContext, IContactNumberService contactNumberService) =>
            {
                var buyerId = httpContext.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.BuyerId = buyerId;
                return await HandleAsync(request, contactNumberService);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumberService)
    {
        try
        {
            var contact = await contactNumberService.RegisterAsync(request.BuyerId, request.PhoneNumber);
            var response = new CreateContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = contact.Id,
                PhoneNumber = contact.PhoneNumber,
                NationalFormat = contact.NationalFormat,
                CountryCode = contact.CountryCode
            };
            return Results.Created($"api/contact-numbers/{contact.Id}", response);
        }
        catch (InvalidContactNumberException ex)
        {
            return Results.BadRequest(new { message = ex.Message, validationErrors = ex.ValidationErrors });
        }
    }
}
