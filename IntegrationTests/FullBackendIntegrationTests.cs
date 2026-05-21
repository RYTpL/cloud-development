using System.Net;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using FluentAssertions;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Полные интеграционные тесты всего бэкенда через .NET Aspire Testing.
/// Поднимают AppHost целиком: Redis + 3 реплики GenerationService + ApiGateway + FileService + LocalStack.
///
/// Требуют запущенного Docker. Запускать отдельно:
///   dotnet test --filter "Category=FullIntegration"
/// </summary>
[Trait("Category", "FullIntegration")]
public class FullBackendIntegrationTests : IAsyncLifetime
{
    private DistributedApplication _app = null!;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.CloudDevelopment_AppHost>();

        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        // Даём всем сервисам время на инициализацию
        await Task.Delay(TimeSpan.FromSeconds(8));
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    // ────────────────── GenerationService ──────────────────

    [Fact]
    public async Task GenerationService_GetContractById_ReturnsValidData()
    {
        using var client = _app.CreateHttpClient("generation-1");

        var response = await client.GetAsync("/contracts/1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var contract = await DeserializeContractAsync(response);
        contract.Id.Should().Be(1);
        contract.ProjectName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GenerationService_SharedRedis_SameIdReturnsSameDataAcrossReplicas()
    {
        using var client1 = _app.CreateHttpClient("generation-1");
        using var client2 = _app.CreateHttpClient("generation-2");
        using var client3 = _app.CreateHttpClient("generation-3");

        const int contractId = 55;

        // Первый запрос — реплика 1 генерирует и кладёт в Redis
        var fromReplica1 = await DeserializeContractAsync(
            await client1.GetAsync($"/contracts/{contractId}"));

        // Реплики 2 и 3 должны прочитать тот же контракт из общего Redis
        var fromReplica2 = await DeserializeContractAsync(
            await client2.GetAsync($"/contracts/{contractId}"));

        var fromReplica3 = await DeserializeContractAsync(
            await client3.GetAsync($"/contracts/{contractId}"));

        fromReplica1.ProjectName.Should().Be(fromReplica2.ProjectName,
            "реплика 2 читает из общего Redis");
        fromReplica1.ProjectName.Should().Be(fromReplica3.ProjectName,
            "реплика 3 читает из общего Redis");
        fromReplica1.Budget.Should().Be(fromReplica2.Budget);
        fromReplica1.ClientCompany.Should().Be(fromReplica3.ClientCompany);
    }

    [Fact]
    public async Task GenerationService_AllReplicas_AreReachable()
    {
        var responses = await Task.WhenAll(
            _app.CreateHttpClient("generation-1").GetAsync("/contracts/1"),
            _app.CreateHttpClient("generation-2").GetAsync("/contracts/1"),
            _app.CreateHttpClient("generation-3").GetAsync("/contracts/1")
        );

        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().Be(HttpStatusCode.OK,
                "каждая реплика должна быть доступна"));
    }

    // ────────────────── ApiGateway ──────────────────

    [Fact]
    public async Task ApiGateway_GetContractById_ProxiesToGenerationService()
    {
        using var gatewayClient = _app.CreateHttpClient("api-gateway");

        var response = await gatewayClient.GetAsync("/contracts/10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var contract = await DeserializeContractAsync(response);
        contract.Id.Should().Be(10);
    }

    [Fact]
    public async Task ApiGateway_MultipleRequests_AllSucceed()
    {
        using var gatewayClient = _app.CreateHttpClient("api-gateway");

        // 8 запросов — один полный цикл WeightedRoundRobin (4+3+1)
        var tasks = Enumerable.Range(1, 8)
            .Select(i => gatewayClient.GetAsync($"/contracts/{i}"))
            .ToList();

        var responses = await Task.WhenAll(tasks);

        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().Be(HttpStatusCode.OK,
                "все запросы через ApiGateway должны проходить"));
    }

    [Fact]
    public async Task ApiGateway_CachedContracts_ReturnConsistentData()
    {
        using var gatewayClient = _app.CreateHttpClient("api-gateway");
        const int id = 333;

        var first = await DeserializeContractAsync(await gatewayClient.GetAsync($"/contracts/{id}"));
        var second = await DeserializeContractAsync(await gatewayClient.GetAsync($"/contracts/{id}"));

        // Второй запрос может попасть на другую реплику, но Redis общий — данные одинаковые
        second.ProjectName.Should().Be(first.ProjectName);
        second.Budget.Should().Be(first.Budget);
        second.Id.Should().Be(first.Id);
    }

    // ────────────────── FileService + SNS + S3 ──────────────────

    [Fact]
    public async Task FileService_Health_ShouldBeReachable()
    {
        using var fileClient = _app.CreateHttpClient("file-service");

        var response = await fileClient.GetAsync("/health");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    // ────────────────── Helper ──────────────────

    private static async Task<SoftwareProjectContract> DeserializeContractAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var contract = JsonSerializer.Deserialize<SoftwareProjectContract>(json, _jsonOptions);
        return contract ?? throw new InvalidOperationException(
            $"Не удалось десериализовать контракт. Тело ответа: {json}");
    }
}
