import { expect, test } from "@playwright/test";

// The walking-skeleton journey: the page is served, the backend is
// reachable, and a created analysis crosses the whole stack
// (API -> Postgres -> Redis -> worker) until it succeeds.
test("a created analysis runs to succeeded", async ({ page }) => {
	await page.goto("/");

	await expect(page.getByText("Backend is up")).toBeVisible();

	await page.getByRole("button", { name: "Create analysis" }).click();

	const status = page.locator(".create-analysis [data-status]");
	await expect(status).toHaveAttribute("data-status", "succeeded", {
		timeout: 15_000,
	});
});
