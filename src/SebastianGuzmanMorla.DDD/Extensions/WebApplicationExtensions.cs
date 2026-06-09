using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Messaging;
using SebastianGuzmanMorla.DDD.Interfaces;
using SebastianGuzmanMorla.DDD.Transformers;

namespace SebastianGuzmanMorla.DDD.Extensions;

public static class WebApplicationExtensions
{
    public static RouteHandlerBuilder MapRequest
    (
        this IEndpointRouteBuilder endpoints,
        RequestMethod method,
        string route,
        Delegate handler
    )
    {
        return method switch
        {
            RequestMethod.Get => endpoints.MapGet(route, handler),
            RequestMethod.Post => endpoints.MapPost(route, handler),
            RequestMethod.Put => endpoints.MapPut(route, handler),
            RequestMethod.Delete => endpoints.MapDelete(route, handler),
            RequestMethod.Patch => endpoints.MapPatch(route, handler),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null)
        };
    }

    public static RouteHandlerBuilder MapRequest<TRequest, TResponse>
    (
        this IEndpointRouteBuilder endpoints,
        RequestMethod method,
        string route,
        string tag
    ) where TRequest : Request<TResponse> where TResponse : Response, new()
    {
        return endpoints
            .MapRequest(method, route, method switch
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

    public static RouteHandlerBuilder MapRequest<TRequest, TResponse, TBinderResponse>
    (
        this IEndpointRouteBuilder endpoints,
        RequestMethod method,
        string prefix,
        string route,
        string tag
    ) where TRequest : Request<TResponse>
      where TResponse : Response, new()
      where TBinderResponse : Response, new()
    {
        string prefixedRoute = $"{prefix}/{route.TrimStart('/')}";
        
        return endpoints
            .MapRequest(method, route, async (IServiceProvider serviceProvider, CancellationToken cancellationToken = default) =>
                {
                    IRequestBinder<TRequest, TBinderResponse> binder = serviceProvider.GetRequiredService<IRequestBinder<TRequest, TBinderResponse>>();
                    
                    (TRequest? request, Response? errorResponse) = await binder.BindAsync(cancellationToken);
                    
                    if (errorResponse is not null)
                    {
                        return Results.Json(errorResponse, statusCode: (int)errorResponse.Status);
                    }
                    
                    return await request!.Handle<TRequest, TResponse>(serviceProvider, cancellationToken).HandleResponse();
                })
            .WithTags(tag)
            .WithDescription(typeof(TRequest).Name)
            .Produces<TResponse>()
            .Produces<Response>(StatusCodes.Status400BadRequest)
            .Produces<Response>(StatusCodes.Status404NotFound)
            .Produces<Response>(StatusCodes.Status500InternalServerError)
            .WithMetadata(new ParametersTransformer<TRequest>(prefixedRoute, method));
    }

    public static async Task<IResult> HandleResponse<TResponse>(this Task<TResponse> responseTask)
        where TResponse : Response
    {
        TResponse response = await responseTask;

        if (response is ResponseFile responseFile)
        {
            return await Task.FromResult(responseFile).HandleResponseFile();
        }

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

        return response switch
        {
            ResponseFileByte fileByte => Results.File(fileByte.Bytes ?? [], fileByte.FileType, fileByte.FileName),
            ResponseFilePath filePath => Results.File(filePath.FilePath ?? string.Empty, filePath.FileType, filePath.FileName),
            _ => Results.NotFound()
        };
    }
}
