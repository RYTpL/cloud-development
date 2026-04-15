namespace FileService.Models;

public record SoftwareProjectContract(
    int Id,
    string ProjectName,
    string ClientCompany,
    string ProjectManager,
    DateOnly StartDate,
    DateOnly PlannedEndDate,
    DateOnly ActualEndDate,
    decimal Budget,
    decimal ActualCost,
    int CompletionPercentage
);