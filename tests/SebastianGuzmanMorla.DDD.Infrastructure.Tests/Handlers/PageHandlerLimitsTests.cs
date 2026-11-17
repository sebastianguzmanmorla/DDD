using System.Net;
using Microsoft.EntityFrameworkCore;
using SebastianGuzmanMorla.DDD.Domain.Messaging;
using SebastianGuzmanMorla.DDD.Infrastructure.Handlers;

namespace SebastianGuzmanMorla.DDD.Infrastructure.Tests.Handlers;

public class PageHandlerLimitsTests
{
    [Theory]
    [InlineData(1, 0, "$.Size")]
    [InlineData(1, 101, "$.Size")]
    [InlineData(1002, 100, "$.Page")]
    [InlineData(int.MaxValue, 100, "$.Page")]
    public async Task InvalidInputsAreRejectedBeforeQueryWithoutAValidator(int page, int size, string error)
    {
        await using var fixture = new DatabaseFixture();
        var handler = new PageHandler(fixture.Services);

        PageResponse response = await handler.Handle(new PageRequest { Page = page, Size = size });

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.NotEmpty(response.Errors![error]);
        Assert.False(handler.BuiltQuery);
    }

    [Theory]
    [InlineData(null, null, 1, 10)]
    [InlineData(-1, 100, 1, 100)]
    [InlineData(1001, 100, 1001, 100)]
    public async Task ValidAndDefaultInputsReachTheExistingQuery(int? page, int? size, int expectedPage, int expectedSize)
    {
        await using var fixture = new DatabaseFixture();
        await fixture.Repository.Add(default, new TestEntity());
        var handler = new PageHandler(fixture.Services);

        PageResponse response = await handler.Handle(new PageRequest { Page = page, Size = size });

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(expectedPage, response.Page);
        Assert.Equal(expectedSize, response.Size);
        Assert.Equal(1, response.Total);
        Assert.Equal(1, response.Pages);
        Assert.Equal(expectedPage == 1 ? 1 : 0, response.Items!.Count);
    }

    private sealed class PageRequest : RequestPage<PageResponse>
    {
        public override void ClearSensitiveProperties() { }
    }

    private sealed class PageResponse : ResponsePage<string>;

    private sealed class PageHandler(IServiceProvider services) :
        RequestPageHandler<TestDbContext, PageRequest, PageResponse, TestEntity, string>(services)
    {
        public bool BuiltQuery { get; private set; }

        protected override IQueryable<string> PageQuery(PageRequest request)
        {
            BuiltQuery = true;
            Assert.NotNull(request.Page);
            Assert.NotNull(request.Size);
            return Queryable.OrderBy(entity => entity.Id).Select(entity => entity.Name);
        }
    }
}
