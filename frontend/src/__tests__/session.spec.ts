import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { resetSessionForTests, useSession } from "../session";

describe("useSession", () => {
	beforeEach(() => {
		resetSessionForTests();
	});

	afterEach(() => {
		vi.unstubAllGlobals();
	});

	it("resolves the signed-in e-mail from the API", async () => {
		vi.stubGlobal(
			"fetch",
			vi.fn().mockResolvedValue({
				ok: true,
				json: () => Promise.resolve({ email: "alice@example.test" }),
			} as Response),
		);
		const session = useSession();

		await session.check();

		expect(session.email.value).toBe("alice@example.test");
		expect(session.checked.value).toBe(true);
	});

	it("treats a 401 as signed out", async () => {
		vi.stubGlobal(
			"fetch",
			vi.fn().mockResolvedValue({ ok: false, status: 401 } as Response),
		);
		const session = useSession();

		await session.check();

		expect(session.email.value).toBeNull();
		expect(session.checked.value).toBe(true);
	});

	it("clears the session on sign-out", async () => {
		vi.stubGlobal(
			"fetch",
			vi
				.fn()
				.mockResolvedValueOnce({
					ok: true,
					json: () => Promise.resolve({ email: "alice@example.test" }),
				} as Response)
				.mockResolvedValueOnce({ ok: true } as Response),
		);
		const session = useSession();
		await session.check();

		await session.signOut();

		expect(session.email.value).toBeNull();
	});
});
