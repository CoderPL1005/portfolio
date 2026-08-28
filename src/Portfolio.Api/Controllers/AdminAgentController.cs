using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Api.Contracts.Common;
using Portfolio.Application.Common.Abstractions.Messaging;
using Portfolio.Application.Common.Models;
using Portfolio.Application.Features.Agent;
using Portfolio.Application.Features.Chat;

namespace Portfolio.Api.Controllers;

[ApiController,Authorize,Route("api/v1/admin/agent")]
public sealed class AdminAgentController(IRequestDispatcher dispatcher):ControllerBase
{
    [HttpGet("settings")]public async Task<ActionResult<ApiResponse<AgentSettingsResult>>> Settings(CancellationToken ct)=>Ok(ApiResponse<AgentSettingsResult>.Ok(await dispatcher.DispatchAsync(new GetAgentSettingsQuery(),ct)));
    [HttpPut("settings")]public async Task<ActionResult<ApiResponse<AgentSettingsResult>>> UpdateSettings(AgentSettingsRequest request,CancellationToken ct)=>Ok(ApiResponse<AgentSettingsResult>.Ok(await dispatcher.DispatchAsync(request.Command(),ct)));
    [HttpGet("knowledge")]public async Task<ActionResult<ApiResponse<PagedResult<KnowledgeListItem>>>> Knowledge([FromQuery]int page=1,[FromQuery]int pageSize=20,[FromQuery]string? sourceType=null,[FromQuery]string? status=null,[FromQuery]string? search=null,CancellationToken ct=default)=>Ok(ApiResponse<PagedResult<KnowledgeListItem>>.Ok(await dispatcher.DispatchAsync(new GetKnowledgeQuery(page,pageSize,sourceType,status,search),ct)));
    [HttpGet("knowledge/{id:guid}")]public async Task<ActionResult<ApiResponse<KnowledgeDetail>>> KnowledgeDetail(Guid id,CancellationToken ct)=>Ok(ApiResponse<KnowledgeDetail>.Ok(await dispatcher.DispatchAsync(new GetKnowledgeDocumentQuery(id),ct)));
    [HttpPost("knowledge/{id:guid}/reindex")]public async Task<ActionResult<ApiResponse<object>>> Reindex(Guid id,CancellationToken ct){var status=await dispatcher.DispatchAsync(new ReindexKnowledgeCommand(id),ct);return Accepted(ApiResponse<object>.Ok(new{status}));}
    [HttpPost("knowledge/reindex-all")]public async Task<ActionResult<ApiResponse<object>>> ReindexAll(CancellationToken ct){var status=await dispatcher.DispatchAsync(new ReindexAllKnowledgeCommand(),ct);return Accepted(ApiResponse<object>.Ok(new{status}));}
    [HttpGet("conversations")]public async Task<ActionResult<ApiResponse<PagedResult<ConversationListItem>>>> Conversations([FromQuery]int page=1,[FromQuery]int pageSize=20,[FromQuery]string? status=null,CancellationToken ct=default)=>Ok(ApiResponse<PagedResult<ConversationListItem>>.Ok(await dispatcher.DispatchAsync(new GetConversationsQuery(page,pageSize,status),ct)));
    [HttpGet("conversations/{id:guid}")]public async Task<ActionResult<ApiResponse<ConversationDetail>>> Conversation(Guid id,CancellationToken ct)=>Ok(ApiResponse<ConversationDetail>.Ok(await dispatcher.DispatchAsync(new GetConversationQuery(id),ct)));
    [HttpPost("conversations/{id:guid}/close")]public async Task<ActionResult<ApiResponse>> Close(Guid id,CancellationToken ct){await dispatcher.DispatchAsync(new CloseConversationCommand(id),ct);return Ok(ApiResponse.Ok());}
}
public sealed record AgentSettingsRequest(bool Enabled,string? Provider,string? ModelName,string? EmbeddingProvider,string? EmbeddingModel,string SystemPrompt,string? WelcomeMessage,string? FallbackMessage,int MaxContextChunks,decimal? MinimumSimilarity,decimal Temperature){public UpdateAgentSettingsCommand Command()=>new(Enabled,Provider,ModelName,EmbeddingProvider,EmbeddingModel,SystemPrompt,WelcomeMessage,FallbackMessage,MaxContextChunks,MinimumSimilarity,Temperature);}
