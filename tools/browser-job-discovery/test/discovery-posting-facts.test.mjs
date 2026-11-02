import test from 'node:test';
import assert from 'node:assert/strict';
import {supportedPostingFacts as extract} from '../src/discovery-posting-facts.mjs';
test('explicit grouped salary, range, unit and period with exact evidence',()=>{
  const raw='Salary: 10,000,000–15,000,000 VND/month';const facts=extract(raw);
  assert.equal(facts.salaryMinimum.value,10000000);assert.equal(facts.salaryMaximum.value,15000000);
  assert.equal(facts.salaryPeriod.value,'MONTHLY');assert.equal(facts.salaryCurrency.value,'VND');
  assert.equal(facts.salaryMinimum.quote,raw);
  assert.equal(extract('Lương: 10-15 triệu VND/tháng').salaryMaximum.value,15000000);
});
for(const raw of ['Salary: 10-15','Salary: 12.34 VND','Salary: 10,00,000 VND','Salary: 20-10 USD','Salary: 10 triệu USD','Salary: 0 USD','Salary: 10 USD\nSalary: 20 USD'])
  test(`salary abstains: ${raw}`,()=>assert.deepEqual(extract(raw),{}));
test('deadline is distinct from publication and requires explicit timezone',()=>{
  assert.equal(extract('Deadline: 2026-11-30T17:00:00+07:00').expiresAt.value,'2026-11-30T17:00:00+07:00');
  for(const raw of ['Published: 2026-11-30T17:00:00Z','Deadline: 2026-11-30','Deadline: 2026-02-30T17:00:00Z','Deadline: 2026-11-30T17:00:00Z\nDeadline: 2026-12-01T17:00:00Z'])assert.deepEqual(extract(raw),{});
});
test('explicit Vietnamese and hyphenated employmentType and workplaceType normalize with exact line quote',()=>{
  const facts=extract('Hình thức làm việc: Toàn thời gian\nNơi làm việc: Hybrid\nHướng dẫn ứng tuyển: Gửi CV qua email hr@example.com');
  assert.equal(facts.employmentType.value,'FULL_TIME');
  assert.equal(facts.employmentType.quote,'Hình thức làm việc: Toàn thời gian');
  assert.equal(facts.workplaceType.value,'HYBRID');
  assert.equal(facts.workplaceType.quote,'Nơi làm việc: Hybrid');
  assert.equal(facts.notes.value,'Gửi CV qua email hr@example.com');
  assert.equal(extract('Employment type: Full-time\nWorkplace type: On-site').employmentType.value,'FULL_TIME');
  assert.equal(extract('Employment type: Full-time\nWorkplace type: On-site').workplaceType.value,'ONSITE');
});
