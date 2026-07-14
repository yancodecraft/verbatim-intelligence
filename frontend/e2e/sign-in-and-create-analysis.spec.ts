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

// The slices 3-6 journey on top of auth: an anonymous visitor is walled off,
// signs in through a real e-mail round-trip, uploads a CSV, maps the verbatim
// column, runs the analysis, sees it cross the whole stack until it succeeds,
// reads the results on the analysis screen, then shares the report, reads it
// anonymously, and revokes the link.
test("sign in, run an analysis, read and share its results", async ({
	page,
	browser,
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

	// The cross-brick contract, exercised for real and read on-screen: the
	// backend wrote the corpus, the worker (stub LLM) wrote themes and
	// citations by reference, and the analysis screen shows them back.
	await page.getByRole("link", { name: "feedback.csv" }).click();
	await expect(page).toHaveURL(/\/analyses\/[0-9a-f-]{36}$/);
	await expect(page.locator(".badge")).toHaveAttribute(
		"data-status",
		"succeeded",
	);

	// Themes weighted by volume, each with its synthesis.
	const theme = page.locator(".theme").first();
	await expect(theme).toBeVisible();
	await expect(theme.locator(".synthesis")).not.toBeEmpty();
	await expect(theme.locator(".count")).toContainText("verbatims");
	await expect(theme.locator(".weight-bar")).toBeVisible();

	// A cited text is a fixture line, word for word, with its source row.
	const quote = theme
		.locator("blockquote")
		.filter({ hasText: "I love the new dashboard" });
	await expect(quote).toBeVisible();
	await expect(quote.locator("cite")).toHaveText("row 1");

	// Share the report: the raw link is shown once, extract its token.
	await page.getByRole("button", { name: "Create share link" }).click();
	const shareUrl = await page.locator(".share-link input").inputValue();
	const shareToken = /\/shared\/([A-Za-z0-9_-]+)$/.exec(shareUrl)?.[1];
	expect(shareToken).toBeDefined();

	// A visitor with no cookies reads the report through the link alone —
	// navigating relative to baseURL, since PublicBaseUrl targets the host.
	const anonymousContext = await browser.newContext();
	const anonymousPage = await anonymousContext.newPage();
	await anonymousPage.goto(`/shared/${shareToken}`);
	await expect(anonymousPage.getByText("feedback.csv")).toBeVisible();
	const sharedTheme = anonymousPage.locator(".theme").first();
	await expect(sharedTheme).toBeVisible();
	await expect(
		sharedTheme
			.locator("blockquote")
			.filter({ hasText: "I love the new dashboard" })
			.locator("cite"),
	).toHaveText("row 1");
	// Read-only and anonymous: no session UI at all.
	await expect(
		anonymousPage.getByRole("button", { name: "Sign out" }),
	).toHaveCount(0);

	// Revocation cuts the link off for real (practices.md: tested e2e).
	await page.getByRole("button", { name: "Revoke" }).click();
	await expect(
		page.getByRole("button", { name: "Create share link" }),
	).toBeVisible();
	await anonymousPage.reload();
	await expect(
		anonymousPage.getByText("This report is not available."),
	).toBeVisible();
	await anonymousContext.close();

	// Right to erasure: deleting the analysis (after an inline confirm) removes
	// it from the account for real (docs/security-review.md, B1).
	await page.getByRole("button", { name: "Delete analysis" }).click();
	await page.getByRole("button", { name: "Confirm deletion" }).click();
	await expect(page).toHaveURL(/\/$/);
	await expect(page.getByText("No analyses yet")).toBeVisible();

	// Sign out drops the session for real.
	await page.getByRole("button", { name: "Sign out" }).click();
	await expect(page).toHaveURL(/\/sign-in$/);
	await page.goto("/");
	await expect(page).toHaveURL(/\/sign-in$/);
});
