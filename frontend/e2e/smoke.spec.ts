import { test, expect } from '@playwright/test';

/**
 * Lightweight smoke against a running stack (default http://localhost:4210).
 * CI skips unless E2E_BASE_URL is set and reachable.
 */
const base = process.env.E2E_BASE_URL || 'http://localhost:4210';

test.describe('OmniAgent Console smoke', () => {
  test('home loads brand and nav targets', async ({ page }) => {
    await page.goto(base + '/');
    await expect(page.getByRole('heading', { name: /OmniAgent Console/i })).toBeVisible({
      timeout: 20_000
    });
    // Sidebar links (Angular may render routerLink as href).
    await expect(page.locator('a[href*="studio"]').first()).toBeVisible();
    await expect(page.locator('a[href*="panel"]').first()).toBeVisible();
  });

  test('docs page reachable', async ({ page }) => {
    await page.goto(base + '/docs');
    await expect(page.locator('body')).toContainText(/OmniAgent|Docs|Panel|Studio/i, {
      timeout: 15_000
    });
  });

  test('history page shell', async ({ page }) => {
    await page.goto(base + '/history');
    await expect(page.getByRole('heading', { name: /History/i })).toBeVisible({
      timeout: 15_000
    });
  });
});
