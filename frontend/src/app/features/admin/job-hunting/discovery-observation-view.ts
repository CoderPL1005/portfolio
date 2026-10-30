import { DiscoveryObservationDetail } from './discovery-review.models';

// Presentation only. Never classify a post, resolve an ID, or manufacture a JD.
export function observationFact(row:DiscoveryObservationDetail,key:string):string {
  const raw=row.source.rawContent??'';
  for(const vacancy of row.vacancies){
    const field=vacancy.fields?.[key], value=field?.value;
    if(field?.status==='EXTRACTED'&&typeof value==='string'&&value.trim()&&raw.toLowerCase().includes(value.toLowerCase())&&
      field.evidence?.some(e=>Number.isInteger(e.start)&&Number.isInteger(e.end)&&e.start>=0&&e.end>e.start&&raw.slice(e.start,e.end)===e.quote&&e.quote.toLowerCase().includes(value.toLowerCase())))return value;
  }
  const names:Record<string,string>={positionTitle:'role',companyName:'company',location:'location'};
  const value=row.review?.extracted?.[names[key]];
  return typeof value==='string'&&value.trim()&&raw.toLowerCase().includes(value.toLowerCase())?value:'';
}
export function observationTitle(row:DiscoveryObservationDetail):string {
  const title=observationFact(row,'positionTitle');
  const value=title||(row.source.rawContent??'').replace(/\s+/gu,' ').trim();
  return value?value.slice(0,120)+(value.length>120?'…':''):'Bài viết chưa có nội dung';
}
export function observationTechnologies(row:DiscoveryObservationDetail):string {
  const raw=(row.source.rawContent??'').toLowerCase();
  const fields=row.vacancies.flatMap(v=>v.fields?.['technologyStack']?.status==='EXTRACTED'&&Array.isArray(v.fields['technologyStack'].value)?v.fields['technologyStack'].value:[]);
  const extracted=row.review?.extracted?.['technologies'];
  return [...new Set([...fields,...(Array.isArray(extracted)?extracted:[])].filter((t):t is string=>typeof t==='string'&&!!t.trim()&&raw.includes(t.toLowerCase())))].slice(0,8).join(' · ');
}
export function observationLink(row:DiscoveryObservationDetail):string[] {
  return ['/admin/job-hunting/discovery/executions',row.executionId,'observations',String(row.itemIndex)];
}
export function observationWarnings(row:DiscoveryObservationDetail):string[] {
  return [...new Set([...(row.sourceWarnings??[]),...(row.review?.sourceWarnings??[]),...(row.source.outcome==='POST_ID_URL_MISMATCH'?['POST_ID_URL_MISMATCH']:[])])];
}
export function observationPublishedAt(row:DiscoveryObservationDetail):string|null {
  const date=row.source.publishedAt;return date&&Number.isFinite(Date.parse(date))?date:null;
}
export function observationEnriched(row:DiscoveryObservationDetail):boolean {
  return row.semanticState==='COMPLETED'&&row.vacancies.some(v=>Object.values(v.fields??{}).some(f=>
    f.origin==='GEMINI'&&f.status==='EXTRACTED'&&f.value!==null&&f.evidence?.some(e=>row.source.rawContent?.slice(e.start,e.end)===e.quote)));
}
export const jdFieldLabels:Record<string,string>={companyName:'Công ty',positionTitle:'Vị trí',location:'Địa điểm',technologyStack:'Công nghệ',
  experienceRequirements:'Yêu cầu',employmentType:'Hình thức làm việc',workplaceType:'Nơi làm việc',salaryMinimum:'Lương tối thiểu',salaryMaximum:'Lương tối đa',
  salaryCurrency:'Đơn vị tiền',salaryPeriod:'Kỳ lương',applicationEmail:'Email ứng tuyển',applicationUrl:'Link ứng tuyển',expiresAt:'Hạn ứng tuyển',notes:'Hướng dẫn / ghi chú',
  seniority:'Cấp độ',experience:'Kinh nghiệm (theo nguồn)',benefits:'Quyền lợi',mandatorySkills:'Kỹ năng bắt buộc',optionalSkills:'Kỹ năng ưu tiên',recruitmentStatus:'Tình trạng tuyển dụng (chưa xác minh)',mentionedPublishedAt:'Ngày đăng được đề cập (không thay thế ngày nguồn)'};
export function observationMissing(row:DiscoveryObservationDetail):string[] {
  if(!row.vacancies.length)return ['Thông tin vị trí'];
  return Object.entries(jdFieldLabels).filter(([key])=>!row.vacancies.every(v=>v.fields?.[key]?.status==='EXTRACTED'&&v.fields[key].value!==null)).map(([,label])=>label);
}
