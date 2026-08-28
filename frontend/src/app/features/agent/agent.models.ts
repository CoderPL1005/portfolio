export interface ChatSource{title:string;sourceType:string;sourceRefId:string|null;projectSlug:string|null;rank:number;similarityScore:number}
export interface ChatAnswer{messageId:string;answer:string;sources:ChatSource[]}
export interface AgentSettings{id:string;name:string;enabled:boolean;provider:string|null;modelName:string|null;embeddingProvider:string|null;embeddingModel:string|null;embeddingDimensions:number;systemPrompt:string;welcomeMessage:string|null;fallbackMessage:string|null;maxContextChunks:number;minimumSimilarity:number|null;temperature:number}
export type AgentSettingsRequest=Omit<AgentSettings,'id'|'name'|'embeddingDimensions'>;
export interface KnowledgeItem{id:string;sourceType:string;sourceRefId:string|null;sourceKey:string;title:string;version:number;indexingStatus:string;chunkCount:number;indexedAt:string|null;updatedAt:string;lastIndexError:string|null}
export interface KnowledgeDetail extends KnowledgeItem{content:string;contentHash:string;chunks:{id:string;chunkIndex:number;content:string;tokenCount:number|null;embeddingModel:string}[]}
export interface ConversationItem{id:string;publicSessionId:string;status:string;startedAt:string;lastMessageAt:string|null;messageCount:number}
export interface ConversationDetail extends ConversationItem{messages:{id:string;role:string;content:string;modelName:string|null;promptTokens:number|null;completionTokens:number|null;latencyMs:number|null;createdAt:string;sources:{title:string;rank:number;similarityScore:number|null}[];feedback:{rating:string;comment:string|null}|null}[]}
