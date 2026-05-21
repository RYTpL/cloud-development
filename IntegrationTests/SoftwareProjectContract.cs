namespace IntegrationTests;

/// <summary>
/// Локальная копия модели контракта для десериализации HTTP-ответов в тестах.
/// Дублирует GenerationService.Models.SoftwareProjectContract намеренно,
/// чтобы тесты не зависели от внутренней модели сервиса.
/// </summary>
public sealed class SoftwareProjectContract
{
    public int Id { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string ClientCompany { get; set; } = string.Empty;
    public string ProjectManager { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly PlannedEndDate { get; set; }
    public DateOnly? ActualEndDate { get; set; }
    public decimal Budget { get; set; }
    public decimal ActualCost { get; set; }
    public int CompletionPercentage { get; set; }
}
