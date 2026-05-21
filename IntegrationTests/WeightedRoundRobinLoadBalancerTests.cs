using ApiGateway.LoadBalancers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Ocelot.Values;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Юнит-тесты для алгоритма балансировки нагрузки WeightedRoundRobin.
/// Вариант 34: веса [4, 3, 1] для трёх инстансов.
/// </summary>
public class WeightedRoundRobinLoadBalancerTests
{
    private static List<ServiceHostAndPort> CreateThreeServices() =>
    [
        new ServiceHostAndPort("localhost", 7130),
        new ServiceHostAndPort("localhost", 7131),
        new ServiceHostAndPort("localhost", 7132)
    ];

    [Fact]
    public async Task LeaseAsync_WithThreeServices_ShouldReturnAllOfThem()
    {
        // Arrange
        var services = CreateThreeServices();
        var balancer = new WeightedRoundRobinLoadBalancer(services);
        var context = new DefaultHttpContext();

        // Act — делаем 8 запросов (сумма весов 4+3+1=8, один полный цикл)
        var results = new List<ServiceHostAndPort>();
        for (var i = 0; i < 8; i++)
        {
            var response = await balancer.LeaseAsync(context);
            results.Add(response.Data);
        }

        // Assert — в одном цикле должны присутствовать все три сервиса
        results.Should().Contain(s => s.DownstreamPort == 7130, "первый инстанс должен быть выбран");
        results.Should().Contain(s => s.DownstreamPort == 7131, "второй инстанс должен быть выбран");
        results.Should().Contain(s => s.DownstreamPort == 7132, "третий инстанс должен быть выбран");
    }

    [Fact]
    public async Task LeaseAsync_WithWeights4_3_1_ShouldDistributeCorrectly()
    {
        // Arrange
        var services = CreateThreeServices();
        var balancer = new WeightedRoundRobinLoadBalancer(services);
        var context = new DefaultHttpContext();

        // Act — 8 запросов = один полный цикл весов [4, 3, 1]
        var counts = new Dictionary<int, int> { [7130] = 0, [7131] = 0, [7132] = 0 };

        for (var i = 0; i < 8; i++)
        {
            var response = await balancer.LeaseAsync(context);
            counts[response.Data.DownstreamPort]++;
        }

        // Assert — ровно 4 запроса на первый, 3 на второй, 1 на третий
        counts[7130].Should().Be(4, "первый инстанс получает вес 4");
        counts[7131].Should().Be(3, "второй инстанс получает вес 3");
        counts[7132].Should().Be(1, "третий инстанс получает вес 1");
    }

    [Fact]
    public async Task LeaseAsync_AfterFullCycle_ShouldRepeatPattern()
    {
        // Arrange
        var services = CreateThreeServices();
        var balancer = new WeightedRoundRobinLoadBalancer(services);
        var context = new DefaultHttpContext();

        // Act — два полных цикла (16 запросов)
        var cycle1 = new Dictionary<int, int> { [7130] = 0, [7131] = 0, [7132] = 0 };
        var cycle2 = new Dictionary<int, int> { [7130] = 0, [7131] = 0, [7132] = 0 };

        for (var i = 0; i < 8; i++)
        {
            var r = await balancer.LeaseAsync(context);
            cycle1[r.Data.DownstreamPort]++;
        }

        for (var i = 0; i < 8; i++)
        {
            var r = await balancer.LeaseAsync(context);
            cycle2[r.Data.DownstreamPort]++;
        }

        // Assert — оба цикла должны иметь одинаковое распределение
        cycle1.Should().BeEquivalentTo(cycle2,
            "паттерн балансировки должен повторяться после полного цикла");
    }

    [Fact]
    public async Task LeaseAsync_WithSingleService_ShouldAlwaysReturnIt()
    {
        // Arrange
        var services = new List<ServiceHostAndPort>
        {
            new ServiceHostAndPort("localhost", 7130)
        };
        var balancer = new WeightedRoundRobinLoadBalancer(services);
        var context = new DefaultHttpContext();

        // Act & Assert
        for (var i = 0; i < 5; i++)
        {
            var response = await balancer.LeaseAsync(context);
            response.Data.DownstreamPort.Should().Be(7130);
        }
    }

    [Fact]
    public void Type_ShouldBeWeightedRoundRobin()
    {
        // Arrange
        var balancer = new WeightedRoundRobinLoadBalancer(CreateThreeServices());

        // Assert
        balancer.Type.Should().Be("WeightedRoundRobin");
    }

    [Fact]
    public async Task LeaseAsync_ConcurrentCalls_ShouldNotThrow()
    {
        // Arrange — проверяем потокобезопасность (lock внутри LeaseAsync)
        var services = CreateThreeServices();
        var balancer = new WeightedRoundRobinLoadBalancer(services);
        var context = new DefaultHttpContext();

        // Act — 50 параллельных вызовов
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => balancer.LeaseAsync(context));

        var results = await Task.WhenAll(tasks);

        // Assert — все вызовы вернули валидные данные без исключений
        results.Should().HaveCount(50);
        results.Should().AllSatisfy(r =>
        {
            r.IsError.Should().BeFalse("балансировщик не должен возвращать ошибки");
            r.Data.Should().NotBeNull();
        });
    }
}
