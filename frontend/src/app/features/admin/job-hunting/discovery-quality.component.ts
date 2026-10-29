import { Component, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { DiscoveryQualityReport } from './discovery-review.models';

@Component({selector:'app-discovery-quality',standalone:true,imports:[DatePipe],template:`
  @if(report();as r){<section class="space-y-4 rounded border p-4" aria-label="Chất lượng trên tập gán nhãn">
    <h2 class="text-xl font-semibold">Chất lượng classifier trên tập được Admin đánh giá</h2>
    @if(r.recoveryCoverage;as recovery){@if(recovery.total){<section><h3>Khôi phục để đánh giá</h3><p>Không có kết quả phân loại gốc</p><p>{{recovery.total}} observations · {{recovery.reviewed}} đã đánh giá. Không tính vào precision/recall của classifier.</p></section>}}
    <p>{{r.notice}}</p><p>Đánh giá lúc {{r.evaluatedAt | date:'medium'}}. Không suy rộng từ mẫu nhỏ ra toàn bộ Facebook.</p>
    <p>Total {{r.counts.total}} · Reviewed {{r.counts.reviewed}} · Unreviewed {{r.counts.unreviewed}} · Confirmed {{r.counts.confirmed}} · Corrected {{r.counts.corrected}} · Uncertain {{r.counts.uncertain}}</p>
    @for(c of r.cohorts;track $index){<section class="space-y-3 border-t pt-3">
      <h3>{{c.classifierVersion}} · Profile {{c.profileFingerprint ?? 'UNKNOWN'}} · Preferences {{c.preferencesFingerprint ?? 'UNKNOWN'}}</h3>
      <p>{{c.counts.total}} observations · {{c.counts.reviewed}} reviewed</p>
      @for(level of [{name:'Observation-level',data:c.observationLevel},{name:'Unique-content-level',data:c.uniqueContentLevel}];track level.name){
        <h4 class="font-semibold">{{level.name}}</h4>
        @for(d of [{name:'Content validity',data:level.data.contentValidity},{name:'Target matching',data:level.data.targetMatching}];track d.name){
          <p>{{d.name}} · Eligible {{d.data.eligibleSamples}} · Abstentions {{d.data.abstentions}} · Unreviewed {{d.data.unreviewed}} · Uncertain {{d.data.uncertain}} · Human conflicts {{d.data.humanConflicts}} · Prediction conflicts {{d.data.predictionConflicts}}</p>
          <details><summary>Confusion matrix (prediction → human)</summary><div class="overflow-x-auto"><table class="w-full text-left"><thead><tr><th>Prediction</th><th>Human</th><th>Count</th></tr></thead><tbody>@for(cell of d.data.matrix;track $index){<tr><td>{{cell.prediction}}</td><td>{{cell.human}}</td><td>{{cell.count}}</td></tr>}</tbody></table></div></details>
          <div class="overflow-x-auto"><table class="w-full text-left"><thead><tr><th>Class</th><th>TP / FP / FN</th><th>Support</th><th>Precision (denominator)</th><th>Recall (denominator)</th></tr></thead><tbody>
          @for(rate of d.data.classes;track rate.label){<tr><td>{{rate.label}}</td><td>{{rate.tp}} / {{rate.fp}} / {{rate.fn}}</td><td>{{rate.support}}</td><td>{{rate.precision ?? 'N/A'}} ({{rate.precisionDenominator}})</td><td>{{rate.recall ?? 'N/A'}} ({{rate.recallDenominator}})</td></tr>}
          </tbody></table></div>
        }
      }
    </section>}
  </section>}
`})
export class DiscoveryQualityComponent { report=input<DiscoveryQualityReport|null>(null); }
