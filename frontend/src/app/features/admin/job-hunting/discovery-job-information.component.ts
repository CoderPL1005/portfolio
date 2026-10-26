import { Component, input } from '@angular/core';

function object(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {};
}
// Presentation only: never infer missing fields or recompute classification.
export function evidenceText(value: unknown): string {
  if (value === null || value === undefined || typeof value === 'boolean') return '';
  if (typeof value === 'string') return value.trim();
  if (typeof value === 'number') return String(value);
  if (Array.isArray(value)) return value.map(evidenceText).filter(Boolean).join(' · ');
  const record = object(value);
  return evidenceText(record['raw']) || evidenceText(record['evidence']) ||
    [record['email'],record['phone'],record['method']].map(evidenceText).filter(Boolean).join(' · ');
}
export function discoveryTitle(extracted: Record<string, unknown>): string {
  const vacancies=extracted['vacancies'];
  return evidenceText(extracted['role']) || evidenceText(Array.isArray(vacancies) ?
    vacancies.map(v => object(v)['title']) : null) || 'Bài viết chưa xác định vị trí';
}
@Component({
  selector: 'app-discovery-job-information', standalone: true,
  template: `
    <dl class="facts">
      @for(field of fields;track field.key){<div><dt>{{field.label}}</dt><dd>{{text(data()[field.key]) || 'Chưa có bằng chứng'}}</dd></div>}
    </dl>
    @for(v of vacancies();track $index){
      <article><h4>{{text(v['title']) || 'Vị trí chưa xác định'}}</h4>
        <dl class="facts"><div><dt>Yêu cầu</dt><dd>{{text(v['requirements']) || 'Chưa có bằng chứng'}}</dd></div>
        <div><dt>Công nghệ</dt><dd>{{text(v['technologies']) || 'Chưa có bằng chứng'}}</dd></div>
        <div><dt>Kinh nghiệm</dt><dd>{{text(v['experience']) || 'Chưa có bằng chứng'}}</dd></div></dl>
      </article>
    }
    <p class="disclaimer">Thông tin trích xuất từ bài viết, chưa được xác minh độc lập.</p>`,
  styles: [`.facts{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1rem;margin:0}.facts>div{min-width:0}dt{font-size:.75rem;color:var(--color-text-muted)}dd{margin:.3rem 0 0;white-space:pre-wrap;overflow-wrap:anywhere;font-size:.9rem}article{border-top:1px solid var(--color-border);margin-top:1rem;padding-top:.75rem}h4{margin:0 0 .75rem}.disclaimer{font-size:.75rem;color:var(--color-text-muted);margin:1.25rem 0 0}@media(max-width:600px){.facts{grid-template-columns:1fr}}`]
})
export class DiscoveryJobInformationComponent {
  data = input.required<Record<string, unknown>>();
  text = evidenceText;
  fields = [{key:'company',label:'Công ty (post-mentioned)'},{key:'role',label:'Vị trí'},
    {key:'location',label:'Địa chỉ'},{key:'experience',label:'Kinh nghiệm'},
    {key:'salary',label:'Mức lương'},{key:'technologies',label:'Công nghệ'},
    {key:'requirements',label:'Yêu cầu chung'},{key:'contact',label:'Liên hệ'}];
  vacancies() { const rows=this.data()['vacancies']; return Array.isArray(rows) ? rows.map(object) : []; }
}
