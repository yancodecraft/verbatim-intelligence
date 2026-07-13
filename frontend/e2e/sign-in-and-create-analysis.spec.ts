import { fileURLToPath } from "node:url";
import { expect, test } from "@playwright/test";

// Mailpit's REST API, reachable from the e2e container on the compose
// network. It reads the mail the backend actually sent.
const MAILPIT_API = process.env.MAILPIT_API_URL ?? "http://mailpit:8025/api/v1";

const FIXTURE_CSV = fileURLToPath(
	new URL("fixtures/feedback.csv", import.meta.url),
);

async function readMailedToken(email: string): Promise<string> {
	// The mail is sent before the API answers 202, but give SMTP a beat.
	await expect(async () => {
		const inbox = await (await fetch(`${MAILPIT_API}/messages`)).json();
		const message = inbox.messages.find(
			(candidate: { To: { Address: string }[] }) =>
				candidate.To[0].Address === email,
		);
		expect(message).toBeDefined();
	}).toPass({ timeout: 10_000 });

	const inbox = await (await fetch(`${MAILPIT_API}/messages`)).json();
	const message = inbox.messages.find(
		(candidate: { To: { Address: string }[] }) =>
			candidate.To[0].Address === email,
	);
	const detail = await (
		await fetch(`${MAILPIT_API}/message/${message.ID}`)
	).json();
	const token = /token=([A-Za-z0-9_-]+)/.exec(detail.Text)?.[1];
	expect(token).toBeDefined();
	return token as string;
}

// The slice-3 journey on top of auth: an anonymous visitor is walled off, signs
// in through a real e-mail round-trip, uploads a CSV, maps the verbatim column,
// runs the analysis, and sees it cross the whole stack until it succeeds.
test("sign in, upload a CSV, and run an analysis to succeeded", async ({
	page,
}) => {
	const email = `e2e-${Date.now()}@example.test`;

	// Anonymous: the home page redirects to sign-in.
	await page.goto("/");
	await expect(page).toHaveURL(/\/sign-in$/);

	// Request the link and pick it up from Mailpit.
	await page.getByLabel("E-mail address").fill(email);
	await page.getByRole("button", { name: "Send me a sign-in link" }).click();
	await expect(page.getByText("Check your inbox")).toBeVisible();
	const token = await readMailedToken(email);

	// The link signs the visitor in and lands them home.
	await page.goto(`/verify?token=${token}`);
	await expect(page).toHaveURL(/\/$/);
	await expect(page.getByText(email)).toBeVisible();
	await expect(page.getByText("Backend is up")).toBeVisible();
	await expect(page.getByText("No analyses yet")).toBeVisible();

	// Start a new analysis: upload the fixture CSV.
	await page.getByRole("link", { name: "New analysis" }).click();
	await expect(page).toHaveURL(/\/analyses\/new$/);
	await page.getByLabel("CSV file").setInputFiles(FIXTURE_CSV);

	// The preview appears; the verbatim column is chosen and the run launched.
	await expect(page.getByText("feedback.csv")).toBeVisible();
	await page.getByRole("radio", { name: "comment" }).check();
	await page.getByRole("button", { name: "Run analysis" }).click();

	// Back on the list, the analysis crosses API -> Postgres -> Redis -> worker
	// until it succeeds (empty and semicolon-bearing rows handled by ingestion).
	await expect(page).toHaveURL(/\/$/);
	const status = page.locator(".analysis-list [data-status]").first();
	await expect(status).toHaveAttribute("data-status", "succeeded", {
		timeout: 15_000,
	});

	// Sign out drops the session for real.
	await page.getByRole("button", { name: "Sign out" }).click();
	await expect(page).toHaveURL(/\/sign-in$/);
	await page.goto("/");
	await expect(page).toHaveURL(/\/sign-in$/);
});
