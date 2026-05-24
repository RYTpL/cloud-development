using Amazon.S3;
using Amazon.S3.Model;
using FileService.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    var config = builder.Configuration;

    return new AmazonS3Client(
        config["AWS:AccessKey"],
        config["AWS:SecretKey"],
        new AmazonS3Config
        {
            ServiceURL = config["AWS:ServiceURL"],
            ForcePathStyle = true
        });
});

builder.Services.AddSingleton<S3StorageService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var s3 = scope.ServiceProvider.GetRequiredService<IAmazonS3>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    var bucket = config["AWS:BucketName"]!;

    try
    {
        await s3.PutBucketAsync(new PutBucketRequest
        {
            BucketName = bucket
        });
    }
    catch
    {
        // bucket already exists
    }
}

app.MapPost("/sns/contracts", async (
    JsonElement contract,
    S3StorageService storage) =>
{
    await storage.SaveContractAsync(contract);

    return Results.Ok();
});

app.Run();