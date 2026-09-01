import { test, expect } from "@playwright/test";

/**
 * Requirement: a visitor to the public meeting list should only see meetings
 * they can actually attend. Meetings that are still Draft or have been
 * Cancelled are not public.
 */
test("a visitor only sees published meetings on the public catalogue", async ({ page }) => {
  await page.goto("/");

  // Exactly the three Published meetings are rendered as cards.
  await expect(page.getByRole("heading", { level: 3 })).toHaveCount(3);

  await expect(page.getByRole("heading", { name: "Frontend Architecture Summit", level: 3 })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Cloud Integration Day", level: 3 })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Product Engineering Meetup", level: 3 })).toBeVisible();

  // The Draft and Cancelled meetings must not appear.
  await expect(page.getByRole("heading", { name: "Distributed Systems Workshop", level: 3 })).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "AI Tools for Developers", level: 3 })).toHaveCount(0);
});
