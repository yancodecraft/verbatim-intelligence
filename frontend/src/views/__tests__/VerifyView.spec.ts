import { flushPromises, mount } from "@vue/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { resetSessionForTests } from "../../session";
import VerifyView from "../VerifyView.vue";

const replaceMock = vi.fn();
let queryToken: string | undefined;

vi.mock("vue-router", () => ({
	useRoute: () => ({ query: { token: queryToken } }),
	useRouter: () => ({ replace: replaceMock }),
	RouterLink: { template: "<a><slot /></a>" },
}));

describe("VerifyView", () => {
	beforeEach(() => {
		resetSessionForTests();
		replaceMock.mockReset();
		queryToken = undefined;
	});

	afterEach(() => {
		vi.unstubAllGlobals();
	});

	it("verifies the token and redirects home", async () => {
		queryToken = "valid-token";
		const fetchMock = vi
			.fn()
			.mockResolvedValueOnce({ ok: true } as Response)
			.mockResolvedValueOnce({
				ok: true,
				json: () => Promise.resolve({ email: "alice@example.test" }),
			} as Response);
		vi.stubGlobal("fetch", fetchMock);

		mount(VerifyView);
		await flushPromises();

		expect(fetchMock).toHaveBeenCalledWith("/api/auth/verify", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify({ token: "valid-token" }),
		});
		expect(replaceMock).toHaveBeenCalledWith({ name: "home" });
	});

	it("shows an error for a rejected token", async () => {
		queryToken = "expired-token";
		vi.stubGlobal(
			"fetch",
			vi.fn().mockResolvedValue({ ok: false, status: 401 } as Response),
		);

		const wrapper = mount(VerifyView);
		await flushPromises();

		expect(wrapper.text()).toContain("invalid or has expired");
		expect(replaceMock).not.toHaveBeenCalled();
	});

	it("shows an error when the link has no token", async () => {
		const fetchMock = vi.fn();
		vi.stubGlobal("fetch", fetchMock);

		const wrapper = mount(VerifyView);
		await flushPromises();

		expect(fetchMock).not.toHaveBeenCalled();
		expect(wrapper.text()).toContain("invalid or has expired");
	});
});
