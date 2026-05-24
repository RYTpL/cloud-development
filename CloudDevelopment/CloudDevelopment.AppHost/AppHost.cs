var builder = DistributedApplication.CreateBuilder(args);

var redis = builder
    .AddRedis("redis")
    .WithRedisInsight();

// Порты для трёх реплик GenerationService
var ports = new[] { 7130, 7131, 7132 };

// Создаём реплики в цикле вместо трёх отдельных переменных
var generationServices = ports
    .Select((port, index) =>
    {
        var name = $"generation-{index + 1}";
        return builder
            .AddProject<Projects.GenerationService>(name)
            .WithReference(redis)
            // Сервис генерации стартует только после того, как Redis готов
            .WaitFor(redis)
            .WithHttpsEndpoint(port: port)
            // Передаём имя реплики через env — стабильно и видно в логах/дашборде
            .WithEnvironment("REPLICA_NAME", name);
    })
    .ToArray();

var gateway = builder
    .AddProject<Projects.ApiGateway>("api-gateway")
    // ApiGateway стартует только после того, как все реплики готовы
    .WaitForCompletion(generationServices[0])
    .WaitForCompletion(generationServices[1])
    .WaitForCompletion(generationServices[2]);

// Клиент стартует после того, как готов ApiGateway
builder.AddProject<Projects.Client_Wasm>("client-wasm")
    .WithReference(gateway)
    .WaitFor(gateway);

var localstack = builder
    .AddContainer("localstack", "localstack/localstack")
    .WithEnvironment("SERVICES", "s3,sns")
    .WithEnvironment("AWS_DEFAULT_REGION", "eu-central-1")
    .WithEnvironment("EDGE_PORT", "4566")
    .WithHttpEndpoint(
        port: 4566,
        targetPort: 4566);

var fileService = builder.AddProject<Projects.FileService>(
    "fileservice");

builder.AddProject<Projects.FileService>("fileservice");

builder.Build().Run();
