export interface DiscoveryRunSummary { runId:string; observations:number; protected:boolean; }
export interface DiscoveryCleanupPlan { dryRun:boolean; runIds:string[]; scopedObservations:number; scopedReviews:number; scopedReviewEvents:number; fingerprint:string; action:string; }
export interface DiscoveryCleanupBackup { snapshot:{schemaVersion:number;runIds:string[];candidates:unknown[];reviews:unknown[];reviewEvents:unknown[]};fingerprint:string; }
export interface DiscoveryCleanupResult { deletedObservations:number;deletedReviews:number;deletedReviewEvents:number; }
export interface JobDataResetPlan { counts:Record<string,number>;totalRecords:number;fingerprint:string; }
export interface JobDataResetResult { deleted:JobDataResetPlan;remaining:JobDataResetPlan; }
