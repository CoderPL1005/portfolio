/** Offline recorded-output adapter, NOT a model or a real service transport. */
export class MockExtractionProvider {
  constructor(output, modelIdentity = 'offline-recorded-mock-v1') {
    this.modelIdentity = modelIdentity;
    this.output = typeof output === 'string' ? output : JSON.stringify(output);
    this.callCount = 0;
  }
  async extract(_request, { signal } = {}) {
    if (signal?.aborted) throw Error('MOCK_ABORTED');
    this.callCount++;
    return this.output;
  }
}
