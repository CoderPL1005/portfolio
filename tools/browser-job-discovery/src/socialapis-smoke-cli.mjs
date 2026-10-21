import { createSocialApisLiveSmoke } from './socialapis-adapter.mjs';

// Not part of npm test/simulate or any workflow. Never accepts a token argument.
const args = process.argv.slice(2);
const flags = ['--live', '--confirm-one-request', '--confirm-fresh-key', '--confirm-free-plan', '--confirm-no-cash', '--debug-response'];
if (args.some(arg => !flags.includes(arg)) || new Set(args).size !== args.length) {
  console.error('INVALID_SMOKE_ARGUMENTS'); process.exitCode = 1;
} else {
  const report = await createSocialApisLiveSmoke({ enabled: args.includes('--live'),
    approvedOneRequest: args.includes('--confirm-one-request'), freshKeyConfirmed: args.includes('--confirm-fresh-key'),
    freePlanConfirmed: args.includes('--confirm-free-plan'), noCashChargeConfirmed: args.includes('--confirm-no-cash'),
    debugResponse: args.includes('--debug-response') }).run();
  console.log(JSON.stringify(report, null, 2));
  if (report.verdict === 'LIVE_BLOCKED') process.exitCode = 1;
}
