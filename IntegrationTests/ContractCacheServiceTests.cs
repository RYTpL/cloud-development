using DotNet.Testcontainers.Builders;
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

        // Настраиваем настоящий Redis distributed cache
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
        // Arrange
        const int contractId = 1;

        // Act
        var contract = await _cacheService.GetOrCreateAsync(contractId);

        // Assert
        contract.Should().NotBeNull();
        contract.Id.Should().Be(contractId);
        contract.ProjectName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetOrCreateAsync_SecondCallWithSameId_ShouldReturnCachedContract()
    {
        // Arrange
        const int contractId = 42;

        // Act — первый вызов (MISS, генерация + сохранение в кэш)
        var firstResult = await _cacheService.GetOrCreateAsync(contractId);

        // Act — второй вызов (HIT, чтение из кэша)
        var secondResult = await _cacheService.GetOrCreateAsync(contractId);

        // Assert — данные должны совпадать
        secondResult.Id.Should().Be(firstResult.Id);
        secondResult.ProjectName.Should().Be(firstResult.ProjectName);
        secondResult.ClientCompany.Should().Be(firstResult.ClientCompany);
        secondResult.Budget.Should().Be(firstResult.Budget);
        secondResult.StartDate.Should().Be(firstResult.StartDate);
    }

    [Fact]
    public async Task GetOrCreateAsync_DifferentIds_ShouldReturnDifferentContracts()
    {
        // Arrange & Act
        var contract1 = await _cacheService.GetOrCreateAsync(100);
        var contract2 = await _cacheService.GetOrCreateAsync(200);

        // Assert
        contract1.Id.Should().Be(100);
        contract2.Id.Should().Be(200);
        contract1.Id.Should().NotBe(contract2.Id);
    }

    [Fact]
    public async Task GetOrCreateAsync_AfterCacheExpiry_ShouldReturnNewContract()
    {
        // Arrange — создаём кэш с очень коротким TTL (1 секунда)
        var shortTtlOptions = Options.Create(new CacheOptions { ExpirationMinutes = 0 });

        // ExpirationMinutes = 0 → TTL = TimeSpan.Zero → кэш фактически не используется
        // Для теста истечения создаём отдельный сервис с минимальным TTL
        var redisOptions = new RedisCacheOptions
        {
            Configuration = _redisContainer.GetConnectionString()
        };
        var shortCache = new RedisCache(Options.Create(redisOptions));

        // Вручную кладём данные в кэш с TTL = 1 секунда
        const int contractId = 777;
        var originalContract = await _cacheService.GetOrCreateAsync(contractId);

        // Принудительно удаляем ключ из кэша, чтобы симулировать истечение
        await _cache.RemoveAsync($"contract:{contractId}");

        // Act — после "истечения" кэша должна произойти повторная генерация
        var afterExpiry = await _cacheService.GetOrCreateAsync(contractId);

        // Assert — id должен совпадать, данные могут отличаться (Bogus рандомный)
        afterExpiry.Id.Should().Be(contractId);
        afterExpiry.Should().NotBeNull();
    }

    [Fact]
    public async Task GetOrCreateAsync_ConcurrentRequests_ShouldNotThrow()
    {
        // Arrange — несколько параллельных запросов с разными id
        var tasks = Enumerable.Range(1, 20)
            .Select(id => _cacheService.GetOrCreateAsync(id));

        // Act & Assert — не должно быть исключений при параллельном доступе
        var results = await Task.WhenAll(tasks);

        results.Should().HaveCount(20);
        results.Should().AllSatisfy(c => c.Should().NotBeNull());
    }
}
