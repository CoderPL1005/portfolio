using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;
using Portfolio.Application.Common.Abstractions.AI;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Features.Agent;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.Infrastructure.AI;

public sealed class PgvectorKnowledgeRetriever(ApplicationDbContext db):IKnowledgeRetriever
{
    public async Task<IReadOnlyCollection<RetrievedKnowledge>> RetrieveAsync(float[] embedding,int topK,decimal? minimumSimilarity,CancellationToken ct=default){if(embedding.Length!=1536)throw new InvalidOperationException("Query embedding must contain 1536 dimensions.");var connection=db.Database.GetDbConnection();if(connection.State!=ConnectionState.Open)await connection.OpenAsync(ct);await using var command=connection.CreateCommand();command.CommandText="""
SELECT kc.id,kd.id,kd.title,kd.source_type,kd.source_ref_id,kd.metadata->>'projectSlug',kc.content,1-(kc.embedding <=> @embedding) AS similarity
FROM knowledge_chunks kc JOIN knowledge_documents kd ON kd.id=kc.knowledge_document_id
WHERE kd.is_active=TRUE AND kd.indexing_status='INDEXED' AND (@minimum IS NULL OR 1-(kc.embedding <=> @embedding)>=@minimum)
ORDER BY kc.embedding <=> @embedding, kc.id LIMIT @top_k
""";var vector=command.CreateParameter();vector.ParameterName="embedding";vector.Value=new Vector(embedding);command.Parameters.Add(vector);var min=command.CreateParameter();min.ParameterName="minimum";min.Value=minimumSimilarity is null?DBNull.Value:minimumSimilarity.Value;command.Parameters.Add(min);var top=command.CreateParameter();top.ParameterName="top_k";top.Value=Math.Clamp(topK,1,20);command.Parameters.Add(top);var results=new List<RetrievedKnowledge>();await using var reader=await command.ExecuteReaderAsync(ct);var rank=1;while(await reader.ReadAsync(ct))results.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetString(3),reader.IsDBNull(4)?null:reader.GetGuid(4),reader.IsDBNull(5)?null:reader.GetString(5),reader.GetString(6),rank++,Convert.ToDecimal(reader.GetValue(7))));return results;}
}
public sealed class KnowledgeIndexer(IApplicationDbContext db,IEmbeddingService embeddings,IOptions<OpenAISettings> options,TimeProvider clock):IKnowledgeIndexer
{
    public async Task IndexPendingAsync(CancellationToken ct=default){var docs=await db.KnowledgeDocuments.Where(x=>x.IsActive&&x.IndexingStatus=="PENDING").OrderBy(x=>x.UpdatedAt).ToListAsync(ct);foreach(var doc in docs){doc.IndexingStatus="INDEXING";await db.SaveChangesAsync(ct);try{var chunks=KnowledgeChunker.Chunk(doc.Content);var generated=new List<KnowledgeChunk>();for(var i=0;i<chunks.Count;i++){var vector=await embeddings.GenerateEmbeddingAsync(chunks[i],ct);if(vector.Length!=1536)throw new InvalidOperationException("Embedding dimension mismatch.");generated.Add(new(){Id=Guid.NewGuid(),KnowledgeDocumentId=doc.Id,ChunkIndex=i,Content=chunks[i],TokenCount=Math.Max(1,chunks[i].Length/4),ContentHash=Hash(chunks[i]),Embedding=vector,EmbeddingModel=options.Value.EmbeddingModel,Metadata=JsonDocument.Parse(doc.Metadata.RootElement.GetRawText()),CreatedAt=clock.GetUtcNow()});}var old=await db.KnowledgeChunks.Where(x=>x.KnowledgeDocumentId==doc.Id).ToListAsync(ct);db.KnowledgeChunks.RemoveRange(old);db.KnowledgeChunks.AddRange(generated);doc.IndexingStatus="INDEXED";doc.IndexedAt=clock.GetUtcNow();doc.LastIndexError=null;await db.SaveChangesAsync(ct);}catch(Exception ex)when(ex is not OperationCanceledException){doc.IndexingStatus="FAILED";doc.LastIndexError=ex is InvalidOperationException?"Indexing configuration or embedding validation failed.":"Embedding provider failed.";await db.SaveChangesAsync(ct);}}}private static string Hash(string x)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(x))).ToLowerInvariant();
}
