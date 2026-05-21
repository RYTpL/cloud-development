extern alias GenerationServiceAssembly;

using FluentAssertions;
using GenerationService.Services;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Юнит-тесты для ContractGeneratorService.
/// Проверяют корректность генерации данных через Bogus без внешних зависимостей.
/// </summary>
public class ContractGeneratorServiceTests
{
    private readonly ContractGeneratorService _generator = new();

    [Fact]
    public void Generate_ShouldReturnContractWithCorrectId()
    {
        var contract = _generator.Generate(42);
        contract.Id.Should().Be(42);
    }

    [Fact]
    public void Generate_ShouldReturnContractWithNonEmptyFields()
    {
        var contract = _generator.Generate(1);

        contract.ProjectName.Should().NotBeNullOrWhiteSpace("название проекта должно быть заполнено");
        contract.ClientCompany.Should().NotBeNullOrWhiteSpace("компания-заказчик должна быть заполнена");
        contract.ProjectManager.Should().NotBeNullOrWhiteSpace("менеджер проекта должен быть указан");
    }

    [Fact]
    public void Generate_ShouldReturnContractWithValidDates()
    {
        var contract = _generator.Generate(1);

        contract.PlannedEndDate.Should().BeOnOrAfter(contract.StartDate,
            "плановая дата завершения должна быть не раньше даты начала");

        if (contract.ActualEndDate.HasValue)
        {
            contract.ActualEndDate.Value.Should().BeOnOrAfter(contract.StartDate,
                "фактическая дата завершения должна быть не раньше даты начала");
        }
    }

    [Fact]
    public void Generate_ShouldReturnContractWithPositiveBudget()
    {
        var contract = _generator.Generate(1);

        contract.Budget.Should().BeGreaterThan(0, "бюджет не может быть нулевым или отрицательным");
        contract.ActualCost.Should().BeGreaterThan(0, "фактические затраты не могут быть нулевыми");
    }

    [Fact]
    public void Generate_ShouldReturnContractWithValidCompletionPercentage()
    {
        var contract = _generator.Generate(1);

        contract.CompletionPercentage.Should().BeInRange(0, 100,
            "процент выполнения должен быть от 0 до 100");
    }

    [Fact]
    public void Generate_MultipleCalls_ShouldReturnDifferentContracts()
    {
        var contracts = Enumerable.Range(1, 10)
            .Select(i => _generator.Generate(i))
            .ToList();

        contracts.Select(c => c.ProjectName)
            .Distinct()
            .Should().HaveCountGreaterThan(1,
                "Bogus должен генерировать разные данные для разных контрактов");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(99999)]
    public void Generate_WithVariousIds_ShouldAlwaysSetCorrectId(int id)
    {
        var contract = _generator.Generate(id);
        contract.Id.Should().Be(id);
    }
}
