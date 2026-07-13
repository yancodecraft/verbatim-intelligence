import { flushPromises, mount } from "@vue/test-utils";
import { afterEach, describe, expect, it, vi } from "vitest";
import SignInView from "../SignInView.vue";

describe("SignInView", () => {
	afterEach(() => {
		vi.unstubAllGlobals();
	});

	it("requests a magic link and tells the user to check their inbox", async () => {
		const fetchMock = vi.fn().mockResolvedValue({ ok: true } as Response);
		vi.stubGlobal("fetch", fetchMock);
		const wrapper = mount(SignInView);

		await wrapper.get("input").setValue("alice@example.test");
		await wrapper.get("form").trigger("submit");
		await flushPromises();

		expect(fetchMock).toHaveBeenCalledWith("/api/auth/magic-link", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify({ email: "alice@example.test" }),
		});
		expect(wrapper.text()).toContain("Check your inbox");
	});

	it("shows an error when the request fails", async () => {
		vi.stubGlobal(
			"fetch",
			vi.fn().mockRejectedValue(new Error("network error")),
		);
		const wrapper = mount(SignInView);

		await wrapper.get("input").setValue("alice@example.test");
		await wrapper.get("form").trigger("submit");
		await flushPromises();

		expect(wrapper.text()).toContain("Something went wrong");
	});
});
