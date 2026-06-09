using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SebastianGuzmanMorla.DDD.Domain.Entities;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace SebastianGuzmanMorla.DDD.Middleware;

public class ExceptionHandlerMiddleware<TContext>(RequestDelegate next)
    where TContext : DbContext
{
    public async Task InvokeAsync(HttpContext context, ILogger<ExceptionHandlerMiddleware<TContext>> logger,
        IServiceProvider serviceProvider)
    {
        try
        {
            await next.Invoke(context);
        }
        catch (TaskCanceledException)
        {
            context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        }
        catch (BadHttpRequestException badHttpRequestException)
        {
            logger.LogError(badHttpRequestException, nameof(ExceptionHandlerMiddleware<TContext>));

            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/json";

            Response response = new()
            {
                Status = HttpStatusCode.BadRequest,
                Message = badHttpRequestException.Message
            };

            await context.Response.WriteAsJsonAsync(response);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, nameof(ExceptionHandlerMiddleware<TContext>));

            Log? log = new()
            {
                Id = Guid.CreateVersion7(),
                Type = LogType.Error,
                Message = exception.ToString(),
                UpdatedAt = DateTime.UtcNow
            };

            await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();

            try
            {
                TContext database = scope.ServiceProvider.GetRequiredService<TContext>();

                await database.Set<Log>().AddAsync(log);

                await database.SaveChangesAsync();
            }
            catch (Exception e)
            {
                log = null;

                logger.LogError(e, nameof(ExceptionHandlerMiddleware<TContext>));
            }

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";

            Response response = new()
            {
                LogId = log?.Id,
                Status = HttpStatusCode.InternalServerError,
                Message = "Error Interno"
            };

            await context.Response.WriteAsJsonAsync(response);
        }
    }
}
