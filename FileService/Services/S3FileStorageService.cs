using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using FileService.Options;
using Microsoft.Extensions.Options;

namespace FileService.Services;

/// <summary>
/// Сервис сохранения контрактов в объектное хранилище (S3 / LocalStack).
/// Сериализует объект в JSON и кладёт по ключу contracts/{id}.json.
/// </summary>
public class S3FileStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;
    private readonly ILogger<S3FileStorageService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public S3FileStorageService(IAmazonS3 s3, IOptions<S3Options> options,
        ILogger<S3FileStorageService> logger)
    {
        _s3 = s3;
        _bucketName = options.Value.BucketName;
        _logger = logger;
    }

    /// <summary>
    /// Сериализует объект в JSON и сохраняет его в S3-бакет.
    /// </summary>
    public async Task SaveAsync<T>(string key, T data, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var stream = new MemoryStream(bytes);

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = stream,
            ContentType = "application/json"
        };

        await _s3.PutObjectAsync(request, ct);

        _logger.LogInformation("Файл {Key} сохранён в бакет {Bucket}", key, _bucketName);
    }

    /// <summary>
    /// Читает JSON-файл из S3 и десериализует его.
    /// </summary>
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await _s3.GetObjectAsync(_bucketName, key, ct);
            using var reader = new StreamReader(response.ResponseStream);
            var json = await reader.ReadToEndAsync(ct);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Файл {Key} не найден в бакете {Bucket}", key, _bucketName);
            return default;
        }
    }

    /// <summary>
    /// Создаёт бакет если он ещё не существует.
    /// </summary>
    public async Task EnsureBucketExistsAsync(CancellationToken ct = default)
    {
        try
        {
            await _s3.PutBucketAsync(_bucketName, ct);
            _logger.LogInformation("Бакет {Bucket} создан", _bucketName);
        }
        catch (AmazonS3Exception ex) when
            (ex.ErrorCode is "BucketAlreadyExists" or "BucketAlreadyOwnedByYou")
        {
            // Бакет уже существует — ок
        }
    }
}
