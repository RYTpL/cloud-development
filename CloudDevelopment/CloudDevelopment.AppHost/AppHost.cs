var builder = DistributedApplication.CreateBuilder(args);

var redis = builder
    .AddRedis("redis")
    .WithRedisInsight();

var generation1 = builder
    .AddProject<Projects.GenerationService>("generation-1")
    .WithReference(redis)
    .WithHttpsEndpoint(port: 7130);

var generation2 = builder
    .AddProject<Projects.GenerationService>("generation-2")
    .WithReference(redis)
    .WithHttpsEndpoint(port: 7131);

var generation3 = builder
    .AddProject<Projects.GenerationService>("generation-3")
    .WithReference(redis)
    .WithHttpsEndpoint(port: 7132);

var gateway = builder
    .AddProject<Projects.ApiGateway>("api-gateway");

builder.AddProject<Projects.Client_Wasm>("client-wasm")
    .WithReference(generation1)
    .WaitFor(generation1);

var fileService = builder
    .AddProject<Projects.FileService>("file-service");

builder.Build().Run();


