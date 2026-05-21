extern alias GenerationServiceAssembly;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Testcontainers.Redis;
using Xunit;
using GenerationProgram = GenerationServiceAssembly::Program;

namespace IntegrationTests;

/// <summary>
/// Интеграционные тесты HTTP-эндпоинтов GenerationService.
/// Поднимают реальный экземпляр сервиса через WebApplicationFactory с Redis в Docker.
///
/// Требование: в проект GenerationService добавить файл ProgramAccessor.cs:
///   public partial class Program { }
/// </summary>
public class GenerationServiceIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private WebApplicationFactory<GenerationProgram> _factory = null!;
    private HttpClient _client = null!;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        _factory = new WebApplicationFactory<GenerationProgram>()
            .WithWebHostBuilder(host =>
            {
                host.UseEnvironment("Test");

                host.ConfigureServices(services =>
                {
                    // Подменяем IDistributedCache на тестовый Redis-контейнер
                    services.RemoveAll<IDistributedCache>();
                    services.RemoveAll<IOptions<RedisCacheOptions>>();

                    services.AddStackExchangeRedisCache(options =>
                    {
                        options.Configuration = _redisContainer.GetConnectionString();
                    });
                });
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _redisContainer.StopAsync();
    }

    // ────────────────── GET /contracts ──────────────────

    [Fact]
    public async Task GetContracts_ShouldReturn200WithValidContract()
    {
        var response = await _client.GetAsync("/contracts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var contract = await DeserializeContractAsync(response);
        contract.ProjectName.Should().NotBeNullOrWhiteSpace();
        contract.ClientCompany.Should().NotBeNullOrWhiteSpace();
        contract.Budget.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetContracts_MultipleCalls_ShouldReturnDifferentContracts()
    {
        var c1 = await DeserializeContractAsync(await _client.GetAsync("/contracts"));
        var c2 = await DeserializeContractAsync(await _client.GetAsync("/contracts"));

        // id генерируется через Random.Shared.Next(1, 100000) — крайне маловероятно совпадение
        var identical = c1.Id == c2.Id && c1.Budget == c2.Budget && c1.StartDate == c2.StartDate;
        identical.Should().BeFalse("два независимых запроса не должны вернуть одинаковый контракт");
    }

    // ────────────────── GET /contracts/{id} ──────────────────

    [Fact]
    public async Task GetContractById_ShouldReturn200WithCorrectId()
    {
        var response = await _client.GetAsync("/contracts/5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var contract = await DeserializeContractAsync(response);
        contract.Id.Should().Be(5);
    }

    [Fact]
    public async Task GetContractById_TwiceSameId_ShouldReturnCachedResult()
    {
        const int id = 999;

        var first = await DeserializeContractAsync(await _client.GetAsync($"/contracts/{id}"));
        var second = await DeserializeContractAsync(await _client.GetAsync($"/contracts/{id}"));

        // Если кэш работает — оба ответа идентичны
        second.Id.Should().Be(first.Id);
        second.ProjectName.Should().Be(first.ProjectName);
        second.Budget.Should().Be(first.Budget);
        second.ClientCompany.Should().Be(first.ClientCompany);
        second.StartDate.Should().Be(first.StartDate);
    }

    [Fact]
    public async Task GetContractById_DifferentIds_ShouldHaveDifferentIds()
    {
        var c10 = await DeserializeContractAsync(await _client.GetAsync("/contracts/10"));
        var c20 = await DeserializeContractAsync(await _client.GetAsync("/contracts/20"));

        c10.Id.Should().Be(10);
        c20.Id.Should().Be(20);
        c10.Id.Should().NotBe(c20.Id);
    }

    [Fact]
    public async Task GetContractById_ContractFieldsAreValid()
    {
        var contract = await DeserializeContractAsync(await _client.GetAsync("/contracts/7"));

        contract.ProjectName.Should().NotBeNullOrWhiteSpace();
        contract.ClientCompany.Should().NotBeNullOrWhiteSpace();
        contract.ProjectManager.Should().NotBeNullOrWhiteSpace();
        contract.Budget.Should().BeGreaterThan(0);
        contract.ActualCost.Should().BeGreaterThan(0);
        contract.CompletionPercentage.Should().BeInRange(0, 100);
        contract.PlannedEndDate.Should().BeOnOrAfter(contract.StartDate);
    }

    // ────────────────── Health ──────────────────

    [Fact]
    public async Task HealthEndpoint_ShouldRespond()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    // ────────────────── Helper ──────────────────

    /// <summary>
    /// Десериализует контракт и бросает исключение если JSON невалидный,
    /// избегая проблем с nullable-доступом к полям.
    /// </summary>
    private static async Task<SoftwareProjectContract> DeserializeContractAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var contract = JsonSerializer.Deserialize<SoftwareProjectContract>(json, _jsonOptions);
        return contract ?? throw new InvalidOperationException(
            $"Не удалось десериализовать контракт. Тело ответа: {json}");
    }
}
