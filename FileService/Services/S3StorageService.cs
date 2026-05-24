using Amazon.S3;
using Amazon.S3.Model;
using System.Text;

namespace FileService.Services;

public class S3StorageService
{
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _configuration;

    public S3StorageService(
        IAmazonS3 s3,
        IConfiguration configuration)
    {
        _s3 = s3;
        _configuration = configuration;
    }

    public async Task SaveContractAsync(object contract)
    {
        var bucket = _configuration["AWS:BucketName"]!;

        var json = System.Text.Json.JsonSerializer.Serialize(contract);

        var key = $"contract-{Guid.NewGuid()}.json";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = stream,
            ContentType = "application/json"
        };

        await _s3.PutObjectAsync(request);
    }
}