// Applies only a guarded workflow patch; no calls to GitHub/Render/Neon/provider.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = process.cwd();
const workflow = path.join(root, '.github', 'workflows', 'job-discovery-daily.yml');
const file = fs.readFileSync(workflow, 'utf8');
const normalized = file.replace(/\r\n/g, '\n');
if (!normalized.startsWith('name: Job Discovery Daily V1\non:\n  schedule:\n') ||
    !normalized.includes("- cron: '11 0 * * *'") ||
    !normalized.includes("- cron: '41 12 * * *'")) {
  throw new Error('BASELINE_MISMATCH: expected V1.2 schedule (commit f187d66); inspect git diff before patching');
}
const head = fs.readFileSync(path.join(here,'workflow-header.yml.fragment'),'utf8');
const classify = fs.readFileSync(path.join(here,'classify-step.yml.fragment'),'utf8');
function replaceExact(src, oldText, newText, name) {
  const count = src.split(oldText).length - 1;
  if (count !== 1) throw new Error(`ANCHOR_MISMATCH ${name}: expected one, found ${count}`);
  return src.replace(oldText, newText);
}
let output = normalized;
const a = output.indexOf('on:\n');
const b = output.indexOf('permissions:\n', a);
if (a === -1 || b === -1 || b < a) throw new Error('YAML_TOP_LEVEL_ANCHOR_MISSING');
output = output.slice(0,a) + head + output.slice(b);
const start = output.indexOf('      - name: Classify trigger and verify cron schedule (pure logic; no provider or DB)\n');
const end = output.indexOf('      - name: Reserved 22:00 slot (future CV-submission threshold)\n', start);
if (start < 0 || end < 0) throw new Error('CLASSIFICATION_STEP_ANCHORS_MISSING');
output = output.slice(0,start) + classify + output.slice(end);
output = replaceExact(output,
  "if: github.event_name == 'schedule' && github.event.schedule == '11 15 * * *'",
  "if: github.event_name == 'workflow_dispatch' && inputs.mode == 'external_scheduled' && inputs.slot == '11 15 * * *' && steps.classify.outputs.authorized == 'true'",
  'reserved-condition');
output = replaceExact(output,
  "if: github.event_name == 'workflow_dispatch' || (github.event_name == 'schedule' && github.event.schedule != '11 15 * * *')",
  "if: github.event_name == 'workflow_dispatch' && (inputs.mode == 'diagnostic_only' || (inputs.mode == 'external_scheduled' && inputs.slot != '11 15 * * *' && steps.classify.outputs.authorized == 'true'))",
  'preflight-condition');
output = replaceExact(output,
  "if: github.event_name == 'workflow_dispatch'\n        env:\n          DISPATCH_MODE:",
  "if: github.event_name == 'workflow_dispatch' && inputs.mode == 'diagnostic_only'\n        env:\n          DISPATCH_MODE:",
  'diagnostic-condition');
output = replaceExact(output,
  "if: github.event_name == 'schedule' && github.event.schedule != '11 15 * * *'",
  "if: github.event_name == 'workflow_dispatch' && inputs.mode == 'external_scheduled' && inputs.slot != '11 15 * * *' && steps.classify.outputs.authorized == 'true'",
  'POST-condition');
output = replaceExact(output,
  'DAILY_SLOT: ${{ github.event.schedule }}',
  'DAILY_SLOT: ${{ inputs.slot }}',
  'daily-slot-env');
output = replaceExact(output,
  'test "$GITHUB_EVENT_NAME" = "schedule"',
  'test "$GITHUB_EVENT_NAME" = "workflow_dispatch"',
  'external-schedule-event-check');
// Safety: no GitHub schedule trigger remains; only authenticated workflow_dispatch.
if (!output.includes('  workflow_dispatch:\n') || output.slice(output.indexOf('on:\n'), output.indexOf('permissions:\n')).includes('  schedule:\n')) {
  throw new Error('UNSAFE_FINAL_TRIGGERS: schedule trigger remained');
}
// Update the existing C# gate that previously asserted GitHub itself owned all cron entries.
const unitPath = path.join(root, 'tests', 'Portfolio.UnitTests', 'PortfolioContent', 'DiscoveryDailySlotsTests.cs');
const unitOriginal = fs.readFileSync(unitPath, 'utf8').replace(/\r\n/g, '\n');
const unitStart = unitOriginal.indexOf('    [Fact]\n    public void Workflow_yaml_defines_eleven_v12_crons_and_gates_daily_post_away_from_diagnostic_mode()\n');
const unitEnd = unitOriginal.indexOf('    private static string GetState(', unitStart);
if (unitStart < 0 || unitEnd < 0) throw new Error('UNIT_TEST_GATE_ANCHORS_MISSING');
const testFragment = fs.readFileSync(path.join(here,'workflow-test.cs.fragment'),'utf8');
const unitNew = unitOriginal.slice(0,unitStart) + testFragment + unitOriginal.slice(unitEnd);
const backupFolder = path.join(root, '.tmp', 'daily-external-scheduler-backup');
fs.mkdirSync(backupFolder,{recursive:true});
const backup = path.join(backupFolder,'job-discovery-daily-v12.yml');
fs.writeFileSync(backup,normalized);
fs.writeFileSync(path.join(backupFolder,'DiscoveryDailySlotsTests-v12.cs'),unitOriginal);
fs.writeFileSync(workflow,output,'utf8');
fs.writeFileSync(unitPath,unitNew,'utf8');
console.log('EXTERNAL_SCHEDULER_WORKFLOW_PATCH_APPLIED');
console.log('Backup:',backup);
console.log('No Git operations, provider calls, Neon access or Cloudflare deployment were made.');
