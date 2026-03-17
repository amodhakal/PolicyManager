using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Services;

namespace PolicyManager.Tests.Services;

public class PolicyHoldersServiceTests : IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly AppDbContext _context;
    private readonly PolicyHoldersService _policyHoldersService;

    public PolicyHoldersServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(opts);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _policyHoldersService = new PolicyHoldersService(_context, _cache);
    }

    public void Dispose()
    {
        _context.Dispose();
        _cache.Dispose();
    }

    private async Task<int> SeedHolder(string first = "Jane", string last = "Doe", string email = "jane@example.com")
    {
        return await _policyHoldersService.Create(new CreatePolicyHolderDto
        {
            FirstName = first,
            LastName = last,
            Email = email
        });
    }

    [Fact]
    public async Task Create_PersistsHolder_ReturnsId()
    {
        var id = await SeedHolder("John", "Smith", "js@test.com");
        var holder = await _context.PolicyHolders.FindAsync(id);

        Assert.NotNull(holder);
        Assert.Equal("John", holder.FirstName);
        Assert.Equal("js@test.com", holder.Email);
    }

    [Fact]
    public async Task GetAll_ReturnsAllHolders()
    {
        await SeedHolder("A", "A", "a@a.com");
        await SeedHolder("B", "B", "b@b.com");

        var result = await _policyHoldersService.GetAll();
        Assert.Equal(2, result.Count());
    }

    [Fact]
    public async Task GetById_ReturnsCorrectDto()
    {
        var id = await SeedHolder("Sue", "Storm", "sue@ff.com");
        var result = await _policyHoldersService.GetById(id);

        Assert.NotNull(result);
        Assert.Equal("Sue", result!.FirstName);
        Assert.Equal("Storm", result.LastName);
        Assert.Equal("sue@ff.com", result.Email);
    }

    [Fact]
    public async Task GetById_NotFound_ReturnsNull()
    {
        var result = await _policyHoldersService.GetById(99999);
        Assert.Null(result);
    }
}