import { defineConfig, devices } from "@playwright/test";

// The e2e suite runs against the live dev stack (make e2e): a Playwright
// container joins the compose network and targets the frontend container.
export default defineConfig({
	testDir: "e2e",
	forbidOnly: !!process.env.CI,
	retries: 0,
	use: {
		baseURL: process.env.PLAYWRIGHT_BASE_URL ?? "http://localhost:5180",
		trace: "retain-on-failure",
	},
	projects: [
		{
			name: "chromium",
			use: { ...devices["Desktop Chrome"] },
		},
	],
});
