/**
 * Vitest/jsdom under Angular's unit-test builder does not always expose
 * a working localStorage. Specs that touch session/api-key storage install this mock.
 */
export function installLocalStorageMock(): void {
  const store = new Map<string, string>();

  const mock: Storage = {
    get length() {
      return store.size;
    },
    clear() {
      store.clear();
    },
    getItem(key: string) {
      return store.has(key) ? store.get(key)! : null;
    },
    key(index: number) {
      return [...store.keys()][index] ?? null;
    },
    removeItem(key: string) {
      store.delete(key);
    },
    setItem(key: string, value: string) {
      store.set(key, String(value));
    }
  };

  Object.defineProperty(globalThis, 'localStorage', {
    configurable: true,
    writable: true,
    value: mock
  });
}
