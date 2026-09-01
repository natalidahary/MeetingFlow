import { test, expect } from "@playwright/test";

/**
 * Requirement: a visitor can pick a published meeting, enter their name and
 * email, choose a ticket type, submit, and see a confirmation.
 */
test("a visitor can register for a published meeting", async ({ page }) => {
  await page.goto("/register");

  await expect(page.getByRole("heading", { name: "Register for a Meeting", level: 1 })).toBeVisible();

  await page.getByLabel("Meeting").selectOption({ index: 1 });
  await page.getByLabel("Your Name").fill("Ada Lovelace");
  await page.getByLabel("Your Email").fill(`e2e-${Date.now()}@meetingflow.test`);
  await page.getByLabel("Ticket Type").selectOption({ index: 1 });

  await page.getByRole("button", { name: "Register" }).click();

  await expect(page.getByText("Registration created successfully!")).toBeVisible();
});
