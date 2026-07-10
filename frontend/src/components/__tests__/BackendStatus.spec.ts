import { flushPromises, mount } from "@vue/test-utils";
import { afterEach, describe, expect, it, vi } from "vitest";
import BackendStatus from "../BackendStatus.vue";

describe("BackendStatus", () => {
	afterEach(() => {
		vi.unstubAllGlobals();
	});

	it("shows the backend as up when /api/health responds", async () => {
		vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true } as Response));

		const wrapper = mount(BackendStatus);
		await flushPromises();

		expect(fetch).toHaveBeenCalledWith("/api/health");
		expect(wrapper.text()).toContain("Backend is up");
	});

	it("shows the backend as down when /api/health fails", async () => {
		vi.stubGlobal(
			"fetch",
			vi.fn().mockRejectedValue(new Error("network error")),
		);

		const wrapper = mount(BackendStatus);
		await flushPromises();

		expect(wrapper.text()).toContain("Backend is down");
	});
});
