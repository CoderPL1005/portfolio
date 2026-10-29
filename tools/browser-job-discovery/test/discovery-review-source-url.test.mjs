
import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizeSocialApisItem } from '../src/socialapis-live-smoke.mjs';
import { buildReviewItems } from '../src/discovery-review.mjs';

for (const [name, url] of [
  ['matching identity', 'https://www.facebook.com/example/posts/9001'],
  ['mismatched identity', 'https://www.facebook.com/example/posts/9002']
]) {
  test(`review/observation provenance parity: ${name}`, () => {
    const row = {
      basic_info: {
        post_id: '9001',
        post_text: 'ABC tuyển Fresher .NET Backend Developer tại Hà Nội.',
        url
      }
    };

    const observation = normalizeSocialApisItem(row);
    const review = buildReviewItems([row])[0];

    // 1. Canonical source URL
    assert.equal(
      review.sourceUrl,
      observation.sourceUrl,
      'ReviewBundle and observation must use the same canonical source URL'
    );

    // 2. Provider post identity
    assert.equal(
      review.providerPostId,
      observation.sourceExternalId,
      'ReviewBundle and observation must use the same provider post ID'
    );

    // 3. Original source content
    assert.equal(
      review.rawContent,
      observation.rawContent,
      'ReviewBundle and observation must preserve the same raw content'
    );
  });
}
