import { createHash } from 'node:crypto';
import { Stop } from './core.mjs';

// Preferences express intent, never proficiency. Stack groups are operator-owned
// query coverage buckets, not a second technology catalogue.
export function targetJobPreferences(value) {
  const strings = items => Array.isArray(items) && items.length > 0 && items.length <= 16 &&
    items.every(s => typeof s === 'string' && s.trim() === s && s.length > 0 && s.length <= 80 && !/[\p{C}]/u.test(s));
  if (!value || value.version !== 1 || value.primaryRole !== 'BACKEND' || typeof value.allowFullStack !== 'boolean' ||
      !strings(value.locations) || !strings(value.seniorities) ||
      !Array.isArray(value.stackGroups) || !value.stackGroups.length || value.stackGroups.length > 8 ||
      value.stackGroups.some(g => !g || !strings(g.technologies) || !['PREFERRED','ACCEPTED'].includes(g.preference)) ||
      !Number.isInteger(value.preferredExperienceMax) || value.preferredExperienceMax < 0 ||
      !Number.isInteger(value.fallbackExperienceMax) || value.fallbackExperienceMax <= value.preferredExperienceMax || value.fallbackExperienceMax > 20)
    throw new Stop('INVALID_TARGET_JOB_PREFERENCES');
  return { version: 1, primaryRole: value.primaryRole, allowFullStack: value.allowFullStack,
    stackGroups: value.stackGroups.map(g => ({ preference:g.preference, technologies:[...new Set(g.technologies)] })),
    seniorities:[...new Set(value.seniorities)], locations:[...new Set(value.locations)],
    preferredExperienceMax:value.preferredExperienceMax, fallbackExperienceMax:value.fallbackExperienceMax };
}

export const preferencesFingerprint = value => createHash('sha256').update(JSON.stringify(targetJobPreferences(value))).digest('hex');
