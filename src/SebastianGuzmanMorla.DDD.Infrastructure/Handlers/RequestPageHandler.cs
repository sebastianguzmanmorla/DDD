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
    protected readonly TContext Context = serviceProvider.GetRequiredService<TContext>();
    protected IQueryable<TEntity> Queryable => Context.Set<TEntity>().AsNoTracking().Where(x => x.DeletedAt == null);

    protected virtual IQueryable<TResponseEntity> PageQuery(TRequest request)
    {
        return Queryable.Cast<TResponseEntity>();
    }

    protected override async Task<TResponse> Execute(TRequest request, CancellationToken cancellationToken = default)
    {
        request.Page ??= 1;
        request.Size ??= 10;

        IQueryable<TResponseEntity> projected = PageQuery(request);

        int total = await projected.CountAsync(cancellationToken);

        List<TResponseEntity> items = await projected
            .Skip((request.Page.Value - 1) * request.Size.Value)
            .Take(request.Size.Value)
            .ToListAsync(cancellationToken);

        TResponse response = new()
        {
            Items = items,
            Page = request.Page.Value,
            Size = request.Size.Value,
            Total = total,
            Pages = total / request.Size.Value + (total % request.Size.Value == 0 ? 0 : 1)
        };

        if (!response.Total.HasValue)
        {
            return response;
        }

        response.Pages = response.Total.Value / request.Size + (response.Total % request.Size == 0 ? 0 : 1);

        return response;
    }
}
