using System.Text;
using ArtasWriter.Application.Options;
using ArtasWriter.Core;
using ArtasWriter.Worker;
using ArtasWriter.Worker.Health;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// Консольный вывод в UTF-8: иначе при перенаправлении в файл (фоновый запуск)
// русский текст логов превращается в "кракозябры" (OEM/ANSI-кодировка).
try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // нет консоли (полностью отвязанный процесс) — не критично
}

var builder = WebApplication.CreateBuilder(args);

// Конфигурация: appsettings.json + переменные окружения (приоритет у env).
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

// Регистрация всех сервисов Writer (композиция вынесена в модуль).
builder.Services.AddWriterModule(builder.Configuration);

var app = builder.Build();

// Порт health-эндпоинтов берём из WriterOptions (единый источник настроек Writer).
var healthPort = app.Services.GetRequiredService<IOptions<WriterOptions>>().Value.HealthPort;
app.Urls.Clear();
app.Urls.Add($"http://localhost:{healthPort}");

// /health — снапшот статуса.
app.MapGet("/health", (IHealthStatusProvider provider) =>
{
    try
    {
        return Results.Json(provider.GetSnapshot());
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "error", error = ex.Message }, statusCode: 500);
    }
});

// /exit — graceful shutdown.
app.MapGet("/exit", (IHostApplicationLifetime lifetime) =>
{
    // Останавливаем приложение в фоне, чтобы успеть ответить клиенту.
    _ = Task.Run(() => lifetime.StopApplication());
    return Results.Json(new { status = "exiting" });
});

app.Logger.LogInformation(
    "Health-эндпоинты: http://localhost:{Port}/health и http://localhost:{Port}/exit",
    healthPort, healthPort);

try
{
    await app.RunAsync();
}
finally
{
    // Флаш невыгруженных метрик/трейсов OpenTelemetry перед выходом.
    Observability.Shutdown();
}
