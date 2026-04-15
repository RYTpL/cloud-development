using Amazon.S3;
using Amazon.S3.Model;
using System.Text.Json;
using FileService.Models;

namespace FileService.Services;

public class S3FileStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;
    private readonly ILogger<S3FileStorageService> _logger;

    public S3FileStorageService(
        IAmazonS3 s3,
        IConfiguration configuration,
        ILogger<S3FileStorageService> logger)
    {
        _s3 = s3;
        _bucketName = configuration["S3__BucketName"] ?? "contracts";
        _logger = logger;
    }

    public async Task EnsureBucketExistsAsync()
    {
        try
        {
            await _s3.PutBucketAsync(new PutBucketRequest
            {
                BucketName = _bucketName,
                UseClientRegion = true
            });
            _logger.LogInformation("Бакет {BucketName} создан", _bucketName);
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Бакет {BucketName} уже существует: {Message}",
                _bucketName, ex.Message);
        }
    }

    public async Task SaveContractAsync(SoftwareProjectContract contract)
    {
        var json = JsonSerializer.Serialize(contract, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var key = $"contracts/{contract.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            ContentBody = json,
            ContentType = "application/json"
        };

        await _s3.PutObjectAsync(request);
        _logger.LogInformation("Файл {Key} сохранён в S3", key);
    }
}