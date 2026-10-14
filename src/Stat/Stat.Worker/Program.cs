using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stat.Application.Options;
using Stat.Worker;

// Консольный вывод в UTF-8: иначе русский текст логов при перенаправлении
// в файл превращается в "кракозябры" (OEM/ANSI-кодировка).
try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // нет консоли (полностью отвязанный процесс) — не критично
}

var builder = Host.CreateApplicationBuilder(args);

// Конфигурация: appsettings.json + аргументы командной строки + переменные окружения.
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddCommandLine(
        args,
        new Dictionary<string, string>
        {
            ["--symbol"] = "Stat:Symbol",
            ["--stop-point"] = "Stat:StopPoint",
            ["--take-point"] = "Stat:TakePoint",
            ["--max-deep"] = "Stat:MaxDeep",
            ["--buffer-size"] = "Stat:BufferSize",
            ["--read-chunk-size"] = "Stat:ReadChunkSize",
            ["--clear"] = "Stat:Clear",
        })
    .AddEnvironmentVariables();

// Регистрация всех сервисов Stat (композиция вынесена в модуль).
builder.Services.AddStatWorker(builder.Configuration);

using var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var options = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<StatOptions>>().Value;

logger.LogInformation(
    "Запуск Stat для {Symbol} (stop={StopPoint}, take={TakePoint}, maxDeep={MaxDeep})",
    options.Symbol, options.StopPoint, options.TakePoint, options.MaxDeep);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var runner = host.Services.GetRequiredService<StatRunner>();
return await runner.RunAsync(cts.Token);
