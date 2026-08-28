using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.AI;

namespace Portfolio.Infrastructure.AI;
public sealed class KnowledgeIndexingWorker(IServiceScopeFactory scopes,IOptions<OpenAISettings> options,ILogger<KnowledgeIndexingWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken){if(!options.Value.EnableIndexingWorker)return;while(!stoppingToken.IsCancellationRequested){try{using var scope=scopes.CreateScope();await scope.ServiceProvider.GetRequiredService<IKnowledgeIndexer>().IndexPendingAsync(stoppingToken);}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}catch(Exception ex){logger.LogError(ex,"Knowledge indexing cycle failed.");}await Task.Delay(TimeSpan.FromSeconds(15),stoppingToken);}}
}
