extern alias GenerationServiceAssembly;

using FluentAssertions;
using GenerationService.Options;
using GenerationService.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Redis;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Интеграционные тесты для ContractCacheService.
/// Используют реальный Redis в Docker-контейнере через Testcontainers.
/// </summary>
public class ContractCacheServiceTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private ContractCacheService _cacheService = null!;
    private IDistributedCache _cache = null!;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        var redisOptions = new RedisCacheOptions
        {
            Configuration = _redisContainer.GetConnectionString()
        };
        _cache = new RedisCache(Options.Create(redisOptions));

        var generator = new ContractGeneratorService();
        var logger = NullLogger<ContractCacheService>.Instance;
        var cacheOptions = Options.Create(new CacheOptions { ExpirationMinutes = 5 });

        _cacheService = new ContractCacheService(_cache, generator, logger, cacheOptions);
    }

    public async Task DisposeAsync()
    {
        await _redisContainer.StopAsync();
    }

    [Fact]
    public async Task GetOrCreateAsync_FirstCall_ShouldReturnGeneratedContract()
    {
        var contract = await _cacheService.GetOrCreateAsync(1);

        contract.Should().NotBeNull();
        contract.Id.Should().Be(1);
        contract.ProjectName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetOrCreateAsync_SecondCallWithSameId_ShouldReturnCachedContract()
    {
        const int contractId = 42;

        // Первый вызов — MISS, генерация + запись в кэш
        var first = await _cacheService.GetOrCreateAsync(contractId);

        // Второй вызов — HIT из Redis
        var second = await _cacheService.GetOrCreateAsync(contractId);

        second.Id.Should().Be(first.Id);
        second.ProjectName.Should().Be(first.ProjectName);
        second.ClientCompany.Should().Be(first.ClientCompany);
        second.Budget.Should().Be(first.Budget);
        second.StartDate.Should().Be(first.StartDate);
    }

    [Fact]
    public async Task GetOrCreateAsync_DifferentIds_ShouldReturnDifferentContracts()
    {
        var contract100 = await _cacheService.GetOrCreateAsync(100);
        var contract200 = await _cacheService.GetOrCreateAsync(200);

        contract100.Id.Should().Be(100);
        contract200.Id.Should().Be(200);
        contract100.Id.Should().NotBe(contract200.Id);
    }

    [Fact]
    public async Task GetOrCreateAsync_AfterManualCacheRemoval_ShouldRegenerateContract()
    {
        const int contractId = 777;

        // Первый вызов — кладём в кэш
        await _cacheService.GetOrCreateAsync(contractId);

        // Вручную удаляем из кэша (симуляция истечения TTL)
        await _cache.RemoveAsync($"contract:{contractId}");

        // Повторный вызов — должна произойти повторная генерация без исключений
        var afterRemoval = await _cacheService.GetOrCreateAsync(contractId);

        afterRemoval.Should().NotBeNull();
        afterRemoval.Id.Should().Be(contractId);
    }

    [Fact]
    public async Task GetOrCreateAsync_ConcurrentRequests_ShouldNotThrow()
    {
        var tasks = Enumerable.Range(1, 20)
            .Select(id => _cacheService.GetOrCreateAsync(id));

        var results = await Task.WhenAll(tasks);

        results.Should().HaveCount(20);
        results.Should().AllSatisfy(c => c.Should().NotBeNull());
    }
}
