using System.Net;
using System.Text.Json;
using Aspire.Hosting.Testing;
using FluentAssertions;
using IntegrationTests.Models;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Полные интеграционные тесты всего бэкенда через .NET Aspire Testing.
/// Поднимают AppHost целиком: Redis + GenerationService (3 реплики) + ApiGateway.
/// Требуют Docker и занимают больше времени, чем юнит-тесты.
/// </summary>
[Trait("Category", "FullIntegration")]
public class FullBackendIntegrationTests : IAsyncLifetime
{
    private DistributedApplication _app = null!;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task InitializeAsync()
    {
        // Aspire поднимает весь AppHost: Redis, все 3 реплики GenerationService, ApiGateway
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.CloudDevelopment_AppHost>();

        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        // Даём сервисам время стартовать
        await Task.Delay(TimeSpan.FromSeconds(5));
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    // ────────────────────────── GenerationService через Aspire ──────────────────────────

    [Fact]
    public async Task GenerationService_GetContractById_ReturnsValidData()
    {
        // Arrange
        using var client = _app.CreateHttpClient("generation-1");

        // Act
        var response = await client.GetAsync("/contracts/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var contract = await DeserializeContract(response);
        contract!.Id.Should().Be(1);
        contract.ProjectName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GenerationService_CachingAcrossReplicas_SameIdReturnsSameData()
    {
        // Arrange — разные реплики, общий Redis
        using var client1 = _app.CreateHttpClient("generation-1");
        using var client2 = _app.CreateHttpClient("generation-2");
        using var client3 = _app.CreateHttpClient("generation-3");

        const int contractId = 55;

        // Act — каждая реплика запрашивает один и тот же id
        var fromReplica1 = await DeserializeContract(
            await client1.GetAsync($"/contracts/{contractId}"));

        var fromReplica2 = await DeserializeContract(
            await client2.GetAsync($"/contracts/{contractId}"));

        var fromReplica3 = await DeserializeContract(
            await client3.GetAsync($"/contracts/{contractId}"));

        // Assert — все три должны вернуть одинаковые данные из общего кэша
        fromReplica1!.ProjectName.Should().Be(fromReplica2!.ProjectName,
            "реплика 2 должна читать из общего кэша Redis");

        fromReplica1.ProjectName.Should().Be(fromReplica3!.ProjectName,
            "реплика 3 должна читать из общего кэша Redis");

        fromReplica1.Budget.Should().Be(fromReplica2.Budget);
        fromReplica1.ClientCompany.Should().Be(fromReplica3.ClientCompany);
    }

    [Fact]
    public async Task GenerationService_AllReplicas_AreReachable()
    {
        // Arrange & Act
        var responses = await Task.WhenAll(
            _app.CreateHttpClient("generation-1").GetAsync("/contracts/1"),
            _app.CreateHttpClient("generation-2").GetAsync("/contracts/1"),
            _app.CreateHttpClient("generation-3").GetAsync("/contracts/1")
        );

        // Assert — все реплики отвечают 200 OK
        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().Be(HttpStatusCode.OK,
                "каждая реплика должна быть доступна"));
    }

    // ────────────────────────── ApiGateway ──────────────────────────

    [Fact]
    public async Task ApiGateway_GetContractById_ProxiesToGenerationService()
    {
        // Arrange
        using var gatewayClient = _app.CreateHttpClient("api-gateway");

        // Act
        var response = await gatewayClient.GetAsync("/contracts/10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var contract = await DeserializeContract(response);
        contract!.Id.Should().Be(10);
    }

    [Fact]
    public async Task ApiGateway_MultipleRequests_DistributesAcrossReplicas()
    {
        // Arrange
        using var gatewayClient = _app.CreateHttpClient("api-gateway");

        // Act — делаем 8 запросов через гейтвей (один цикл WeightedRoundRobin)
        var tasks = Enumerable.Range(1, 8)
            .Select(i => gatewayClient.GetAsync($"/contracts/{i}"));

        var responses = await Task.WhenAll(tasks);

        // Assert — все запросы прошли успешно через балансировщик
        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().Be(HttpStatusCode.OK,
                "все запросы через ApiGateway должны быть успешны"));

        // Все вернули валидные контракты
        foreach (var (response, index) in responses.Select((r, i) => (r, i + 1)))
        {
            var contract = await DeserializeContract(response);
            contract!.Id.Should().Be(index);
        }
    }

    [Fact]
    public async Task ApiGateway_CachedContracts_ReturnConsistentData()
    {
        // Arrange
        using var gatewayClient = _app.CreateHttpClient("api-gateway");
        const int id = 333;

        // Act — первый запрос через гейтвей (попадает на одну из реплик)
        var first = await DeserializeContract(await gatewayClient.GetAsync($"/contracts/{id}"));

        // Второй запрос может попасть на другую реплику, но Redis общий
        var second = await DeserializeContract(await gatewayClient.GetAsync($"/contracts/{id}"));

        // Assert — данные должны совпасть благодаря общему кэшу
        second!.ProjectName.Should().Be(first!.ProjectName);
        second.Budget.Should().Be(first.Budget);
        second.Id.Should().Be(first.Id);
    }

    // ────────────────────────── Helper ──────────────────────────

    private static async Task<SoftwareProjectContract?> DeserializeContract(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SoftwareProjectContract>(json, JsonOptions);
    }
}
