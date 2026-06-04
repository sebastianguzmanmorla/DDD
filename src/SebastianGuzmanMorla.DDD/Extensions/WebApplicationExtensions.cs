using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace SebastianGuzmanMorla.DDD.Extensions;

public static class WebApplicationExtensions
{
    public static RouteHandlerBuilder MapRequest
    (
        this IEndpointRouteBuilder endpoints,
        RequestMethod method,
        string pattern,
        Delegate handler
    )
    {
        return method switch
        {
            RequestMethod.Get => endpoints.MapGet(pattern, handler),
            RequestMethod.Post => endpoints.MapPost(pattern, handler),
            RequestMethod.Put => endpoints.MapPut(pattern, handler),
            RequestMethod.Delete => endpoints.MapDelete(pattern, handler),
            RequestMethod.Patch => endpoints.MapPatch(pattern, handler),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null)
        };
    }

    public static RouteHandlerBuilder MapRequest<TRequest, TResponse>
    (
        this IEndpointRouteBuilder endpoints,
        RequestMethod method,
        string pattern,
        string tag
    ) where TRequest : Request<TResponse> where TResponse : Response, new()
    {
        return endpoints
            .MapRequest(method, pattern, method switch
            {
                RequestMethod.Get or RequestMethod.Delete => async ([AsParameters] TRequest request, IServiceProvider serviceProvider,
                        CancellationToken cancellationToken = default)
                    => await request.Handle<TRequest, TResponse>(serviceProvider, cancellationToken).HandleResponse(),
                _ => async ([FromBody] TRequest request, IServiceProvider serviceProvider,
                        CancellationToken cancellationToken = default)
                    => await request.Handle<TRequest, TResponse>(serviceProvider, cancellationToken).HandleResponse()
            })
            .WithTags(tag)
            .WithDescription(typeof(TRequest).Name)
            .Produces<TResponse>()
            .Produces<Response>(StatusCodes.Status400BadRequest)
            .Produces<Response>(StatusCodes.Status404NotFound)
            .Produces<Response>(StatusCodes.Status500InternalServerError);
    }

    public static async Task<IResult> HandleResponse<TResponse>(this Task<TResponse> responseTask)
        where TResponse : Response
    {
        TResponse response = await responseTask;

        return response.Status switch
        {
            HttpStatusCode.OK => Results.Json(response, statusCode: (int)response.Status),
            HttpStatusCode.Redirect => Results.Redirect(response.Message),
            _ => Results.Json(response, statusCode: (int)response.Status)
        };
    }

    public static async Task<IResult> HandleResponseFile<TResponseFile>(this Task<TResponseFile> responseTask)
        where TResponseFile : ResponseFile
    {
        TResponseFile response = await responseTask;

        if (response.Status != HttpStatusCode.OK)
        {
            return Results.Json((Response)response, statusCode: (int)response.Status);
        }

        if (response is ResponseFileByte fileByte)
        {
            return Results.File(fileByte.Bytes ?? Array.Empty<byte>(), fileByte.FileType, fileByte.FileName);
        }

        if (response is ResponseFilePath filePath)
        {
            return Results.File(filePath.FilePath ?? string.Empty, filePath.FileType, filePath.FileName);
        }

        return Results.NotFound();
    }
}
