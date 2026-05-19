using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace FileService.Services;

public class S3Uploader
{
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<S3Uploader> _logger;
    private readonly string _bucketName;

    public S3Uploader(IConfiguration config, ILogger<S3Uploader> logger)
    {
        _logger = logger;

        var localstackUrl = config["LocalStack:Url"] ?? "http://localhost:4566";

        _s3Client = new AmazonS3Client(
            new BasicAWSCredentials("test", "test"),
            new AmazonS3Config
            {
                ServiceURL = localstackUrl,
                RegionEndpoint = RegionEndpoint.USEast1,
                ForcePathStyle = true
            });

        _bucketName = config["S3:BucketName"] ?? "contracts";
    }

    public async Task UploadContractsAsync(string contractsJson)
    {
        try
        {
            var exists = await _s3Client.DoesS3BucketExistAsync(_bucketName);
            if (!exists)
            {
                await _s3Client.PutBucketAsync(new PutBucketRequest
                {
                    BucketName = _bucketName
                });
                _logger.LogInformation("Created S3 bucket: {Bucket}", _bucketName);
            }

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var key = $"contracts_{timestamp}.json";

            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                ContentBody = contractsJson,
                ContentType = "application/json"
            };

            await _s3Client.PutObjectAsync(request);
            _logger.LogInformation("Uploaded {Key} to S3", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload to S3");
        }
    }
}