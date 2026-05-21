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
        new("localhost", 7130),
        new("localhost", 7131),
        new("localhost", 7132)
    ];

    [Fact]
    public async Task LeaseAsync_WithThreeServices_ShouldReturnAllOfThem()
    {
        var balancer = new WeightedRoundRobinLoadBalancer(CreateThreeServices());
        var context = new DefaultHttpContext();

        var results = new List<ServiceHostAndPort>();
        for (var i = 0; i < 8; i++)
        {
            var response = await balancer.LeaseAsync(context);
            results.Add(response.Data);
        }

        results.Should().Contain(s => s.DownstreamPort == 7130, "первый инстанс должен быть выбран");
        results.Should().Contain(s => s.DownstreamPort == 7131, "второй инстанс должен быть выбран");
        results.Should().Contain(s => s.DownstreamPort == 7132, "третий инстанс должен быть выбран");
    }

    [Fact]
    public async Task LeaseAsync_WithWeights4_3_1_ShouldDistributeCorrectly()
    {
        var balancer = new WeightedRoundRobinLoadBalancer(CreateThreeServices());
        var context = new DefaultHttpContext();

        // 8 запросов = один полный цикл весов [4, 3, 1]
        var counts = new Dictionary<int, int> { [7130] = 0, [7131] = 0, [7132] = 0 };

        for (var i = 0; i < 8; i++)
        {
            var response = await balancer.LeaseAsync(context);
            counts[response.Data.DownstreamPort]++;
        }

        counts[7130].Should().Be(4, "первый инстанс получает вес 4");
        counts[7131].Should().Be(3, "второй инстанс получает вес 3");
        counts[7132].Should().Be(1, "третий инстанс получает вес 1");
    }

    [Fact]
    public async Task LeaseAsync_AfterFullCycle_ShouldRepeatPattern()
    {
        var balancer = new WeightedRoundRobinLoadBalancer(CreateThreeServices());
        var context = new DefaultHttpContext();

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

        cycle1.Should().BeEquivalentTo(cycle2,
            "паттерн балансировки должен повторяться после полного цикла");
    }

    [Fact]
    public async Task LeaseAsync_WithSingleService_ShouldAlwaysReturnIt()
    {
        var balancer = new WeightedRoundRobinLoadBalancer(
        [
            new("localhost", 7130)
        ]);
        var context = new DefaultHttpContext();

        for (var i = 0; i < 5; i++)
        {
            var response = await balancer.LeaseAsync(context);
            response.Data.DownstreamPort.Should().Be(7130);
        }
    }

    [Fact]
    public void Type_ShouldBeWeightedRoundRobin()
    {
        var balancer = new WeightedRoundRobinLoadBalancer(CreateThreeServices());
        balancer.Type.Should().Be("WeightedRoundRobin");
    }

    [Fact]
    public async Task LeaseAsync_ConcurrentCalls_ShouldNotThrow()
    {
        var balancer = new WeightedRoundRobinLoadBalancer(CreateThreeServices());
        var context = new DefaultHttpContext();

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => balancer.LeaseAsync(context));

        var results = await Task.WhenAll(tasks);

        results.Should().HaveCount(50);
        results.Should().AllSatisfy(r =>
        {
            r.IsError.Should().BeFalse("балансировщик не должен возвращать ошибки");
            r.Data.Should().NotBeNull();
        });
    }
}
