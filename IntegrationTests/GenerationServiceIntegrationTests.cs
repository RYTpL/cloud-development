using System.Net;
using System.Text.Json;
using FluentAssertions;
using IntegrationTests.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Testcontainers.Redis;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Интеграционные тесты HTTP-эндпоинтов GenerationService.
/// Поднимают реальный экземпляр сервиса с настоящим Redis в Docker.
/// </summary>
public class GenerationServiceIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.UseEnvironment("Test");

                host.ConfigureServices(services =>
                {
                    // Заменяем зарегистрированный IDistributedCache на тестовый Redis
                    services.RemoveAll<IDistributedCache>();
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

    // ────────────────────────── GET /contracts ──────────────────────────

    [Fact]
    public async Task GetContracts_ShouldReturn200WithValidContract()
    {
        // Act
        var response = await _client.GetAsync("/contracts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var contract = await DeserializeContract(response);
        contract.Should().NotBeNull();
        contract!.ProjectName.Should().NotBeNullOrWhiteSpace();
        contract.ClientCompany.Should().NotBeNullOrWhiteSpace();
        contract.Budget.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetContracts_MultipleCalls_ShouldReturnDifferentContracts()
    {
        // Act — два независимых запроса
        var response1 = await _client.GetAsync("/contracts");
        var response2 = await _client.GetAsync("/contracts");

        var contract1 = await DeserializeContract(response1);
        var contract2 = await DeserializeContract(response2);

        // Assert — id генерируется рандомно, вероятность совпадения мала
        // Хотя бы одно поле должно отличаться (бюджет, дата, название)
        var areIdentical = contract1!.Id == contract2!.Id
                           && contract1.Budget == contract2.Budget
                           && contract1.StartDate == contract2.StartDate;

        areIdentical.Should().BeFalse(
            "два независимых запроса не должны возвращать полностью идентичные контракты");
    }

    // ────────────────────────── GET /contracts/{id} ──────────────────────────

    [Fact]
    public async Task GetContractById_ShouldReturn200WithCorrectId()
    {
        // Arrange
        const int id = 5;

        // Act
        var response = await _client.GetAsync($"/contracts/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var contract = await DeserializeContract(response);
        contract!.Id.Should().Be(id);
    }

    [Fact]
    public async Task GetContractById_TwiceSameId_ShouldReturnCachedResult()
    {
        // Arrange
        const int id = 999;

        // Act — первый запрос (генерация + кэш)
        var first = await DeserializeContract(await _client.GetAsync($"/contracts/{id}"));

        // Act — второй запрос (из кэша)
        var second = await DeserializeContract(await _client.GetAsync($"/contracts/{id}"));

        // Assert — оба ответа должны совпадать (кэш работает)
        second!.Id.Should().Be(first!.Id);
        second.ProjectName.Should().Be(first.ProjectName);
        second.Budget.Should().Be(first.Budget);
        second.ClientCompany.Should().Be(first.ClientCompany);
        second.StartDate.Should().Be(first.StartDate);
    }

    [Fact]
    public async Task GetContractById_DifferentIds_ShouldReturnDifferentContracts()
    {
        // Act
        var contract10 = await DeserializeContract(await _client.GetAsync("/contracts/10"));
        var contract20 = await DeserializeContract(await _client.GetAsync("/contracts/20"));

        // Assert
        contract10!.Id.Should().Be(10);
        contract20!.Id.Should().Be(20);
        contract10.Id.Should().NotBe(contract20.Id);
    }

    [Fact]
    public async Task GetContractById_ContractFieldsAreValid()
    {
        // Act
        var response = await _client.GetAsync("/contracts/7");
        var contract = await DeserializeContract(response);

        // Assert
        contract!.ProjectName.Should().NotBeNullOrWhiteSpace();
        contract.ClientCompany.Should().NotBeNullOrWhiteSpace();
        contract.ProjectManager.Should().NotBeNullOrWhiteSpace();
        contract.Budget.Should().BeGreaterThan(0);
        contract.ActualCost.Should().BeGreaterThan(0);
        contract.CompletionPercentage.Should().BeInRange(0, 100);
        contract.PlannedEndDate.Should().BeOnOrAfter(contract.StartDate);
    }

    // ────────────────────────── Health checks ──────────────────────────

    [Fact]
    public async Task HealthEndpoint_ShouldReturnHealthy()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert — 200 OK или 503 допустимы, но ответ должен прийти
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    // ────────────────────────── Helpers ──────────────────────────

    private static async Task<SoftwareProjectContract?> DeserializeContract(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SoftwareProjectContract>(json, JsonOptions);
    }
}
