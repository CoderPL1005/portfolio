export type MediaType = 'IMAGE' | 'DOCUMENT' | 'CV' | 'OTHER';
export interface MediaAsset { id:string; fileName:string; mimeType:string|null; fileSize:number|null; mediaType:MediaType; storageKey:string; publicUrl:string; altText:string|null; createdAt:string; updatedAt:string }
export interface MediaUpdate { altText:string|null; mediaType:MediaType }
