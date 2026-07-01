using Microsoft.AspNetCore.Antiforgery;

namespace Pico.Api.Endpoints;

public static class AntiforgeryEndpointExtensions
{
    public static RouteGroupBuilder RequireAntiforgeryForUnsafeMethods(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;
            if (!IsSafeMethod(httpContext.Request.Method))
            {
                var antiforgery = httpContext.RequestServices.GetRequiredService<IAntiforgery>();
                try
                {
                    await antiforgery.ValidateRequestAsync(httpContext);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.Forbid();
                }
            }

            return await next(context);
        });

        return group;
    }

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) ||
        HttpMethods.IsHead(method) ||
        HttpMethods.IsOptions(method) ||
        HttpMethods.IsTrace(method);
}
