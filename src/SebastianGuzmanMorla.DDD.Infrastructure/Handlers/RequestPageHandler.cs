using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SebastianGuzmanMorla.DDD.Domain.Entities;
using SebastianGuzmanMorla.DDD.Domain.Messaging;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Handlers;

public abstract class RequestPageHandler<TContext, TRequest, TResponse, TEntity, TResponseEntity>(
    IServiceProvider serviceProvider
) : RequestHandler<TContext, TRequest, TResponse>(serviceProvider)
    where TContext : DbContext
    where TRequest : RequestPage<TResponse>
    where TResponse : ResponsePage<TResponseEntity>, new()
    where TEntity : Entity
    where TResponseEntity : notnull
{
    private const int MaxPageSize = 100;
    private const int MaxPageOffset = 100_000;

    protected readonly TContext Context = serviceProvider.GetRequiredService<TContext>();
    protected IQueryable<TEntity> Queryable => Context.Set<TEntity>().AsNoTracking().Where(x => x.DeletedAt == null);

    protected virtual IQueryable<TResponseEntity> PageQuery(TRequest request)
    {
        return Queryable.Cast<TResponseEntity>();
    }

    protected override async Task<TResponse> Execute(TRequest request, CancellationToken cancellationToken = default)
    {
        int page = request.Page ??= 1;
        int size = request.Size ??= 10;

        if (size is < 1 or > MaxPageSize)
        {
            return InvalidPage(nameof(request.Size), $"Page size must be between 1 and {MaxPageSize}.");
        }

        long offset = (page - 1L) * size;
        if (offset > MaxPageOffset)
        {
            return InvalidPage(nameof(request.Page), $"Page offset must not exceed {MaxPageOffset}.");
        }

        request.Page = page;
        request.Size = size;
        IQueryable<TResponseEntity> projected = PageQuery(request);

        int total = await projected.CountAsync(cancellationToken);

        List<TResponseEntity> items = await projected
            .Skip((int)offset)
            .Take(size)
            .ToListAsync(cancellationToken);

        TResponse response = new()
        {
            Items = items,
            Page = page,
            Size = size,
            Total = total,
            Pages = total / size + (total % size == 0 ? 0 : 1)
        };

        if (!response.Total.HasValue)
        {
            return response;
        }

        response.Pages = response.Total.Value / size + (response.Total % size == 0 ? 0 : 1);

        return response;
    }

    private static TResponse InvalidPage(string property, string message) => new()
    {
        Status = HttpStatusCode.BadRequest,
        Message = "Validation errors",
        Errors = new() { [$"$.{property}"] = [message] }
    };
}
