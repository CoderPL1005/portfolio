import { fingerprint, fold } from './candidate-technology-profile.mjs';
import { extractVacancies } from './vacancy-extraction.mjs';

const normalize=text=>text.normalize('NFC').replace(/\r\n?/gu,'\n').replace(/[\t ]+/gu,' ').replace(/\n+/gu,'\n').trim();
const vacanciesOf=job=>Array.isArray(job.vacancies) ? job.vacancies : typeof job.body==='string' && job.body.length<=24000
  ? extractVacancies(job.body,{sourceUrl:job.sourceUrl,publishedAt:job.publishedAt??null}).vacancies : [];
export function opportunityIds(job) {
  // Full post content (including numbers/deadlines/contacts) participates: sharing
  // a footer, company or one technology never merges two different vacancies.
  const content=normalize(job.body??'');
  return vacanciesOf(job).map((v,index)=>typeof v.title==='string' && v.title.trim() && content.length>=80
    ? `content:${fingerprint([content,normalize(v.evidence?.body??''),normalize(v.title),index])}`
    : `source:${job.sourceIdentity}:${index}`);
}
export function buildContentRegistry(jobs) {
  const groups=new Map();
  for(const job of jobs) opportunityIds(job).forEach((identity,index)=>{
    const group=groups.get(identity)??{identity,confidence:identity.startsWith('content:')?'EXACT_CONTENT_REPOST_GROUP':'SOURCE_ONLY',sources:[]};
    group.sources.push({sourceIdentity:job.sourceIdentity,postId:job.id,sourceUrl:job.canonicalSourceUrl??job.sourceUrl,vacancyIndex:index});
    groups.set(identity,group);
  });
  return {version:'content-opportunities-v1',groups:[...groups.values()]};
}
const tokens=body=>new Set(fold(normalize(body)).match(/[\p{L}\p{N}#+.]+/gu)??[]);
export function nearDuplicateCandidates(pageJobs, historicalJobs) {
  const pairs=[], seen=new Set();
  // Detection only, never auto-merge. Bounded recent pool; unknown campaign dates
  // make a near match insufficient proof of identical opportunity membership.
  const pool=historicalJobs.slice(-100).map(j=>({job:j,tokens:tokens(j.body??'')}));
  for(const current of pageJobs) {
    const a=tokens(current.body??''); if(a.size<20)continue;
    for(const {job:previous,tokens:b} of pool) {
      if(current.sourceIdentity===previous.sourceIdentity || normalize(current.body??'')===normalize(previous.body??''))continue;
      const key=[current.sourceIdentity,previous.sourceIdentity].sort().join('|');if(seen.has(key))continue;seen.add(key);
      const titles=j=>vacanciesOf(j).map(v=>fold(v.title??'')).join('|');
      const numbers=s=>(s.match(/\d+/gu)??[]).join('|');
      if(!titles(current) || titles(current)!==titles(previous) || numbers(current.body??'')!==numbers(previous.body??''))continue;
      const intersection=[...a].filter(t=>b.has(t)).length;
      const score=intersection/(a.size+b.size-intersection);
      if(score>=0.92)pairs.push({sources:[current.sourceIdentity,previous.sourceIdentity].sort(),score,decision:'REVIEW_ONLY_NOT_MERGED'});
      if(pairs.length>=100)return pairs;
    }
  }
  return pairs;
}
