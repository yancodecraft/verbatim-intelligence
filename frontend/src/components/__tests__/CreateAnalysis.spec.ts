import { flushPromises, mount } from "@vue/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import CreateAnalysis from "../CreateAnalysis.vue";

const ANALYSIS_ID = "0198b1c2-0000-7000-8000-000000000001";

function jsonResponse(status: string): Response {
	return {
		ok: true,
		json: () =>
			Promise.resolve({
				id: ANALYSIS_ID,
				status,
				createdAt: "2026-07-10T00:00:00Z",
			}),
	} as Response;
}

describe("CreateAnalysis", () => {
	beforeEach(() => {
		vi.useFakeTimers();
	});

	afterEach(() => {
		vi.useRealTimers();
		vi.unstubAllGlobals();
	});

	it("creates an analysis and shows its status", async () => {
		const fetchMock = vi.fn().mockResolvedValue(jsonResponse("pending"));
		vi.stubGlobal("fetch", fetchMock);
		const wrapper = mount(CreateAnalysis);

		await wrapper.get("button").trigger("click");
		await flushPromises();

		expect(fetchMock).toHaveBeenCalledWith("/api/analyses", {
			method: "POST",
		});
		expect(wrapper.text()).toContain("pending");
	});

	it("polls until the analysis succeeds", async () => {
		const fetchMock = vi
			.fn()
			.mockResolvedValueOnce(jsonResponse("pending"))
			.mockResolvedValueOnce(jsonResponse("running"))
			.mockResolvedValueOnce(jsonResponse("succeeded"));
		vi.stubGlobal("fetch", fetchMock);
		const wrapper = mount(CreateAnalysis);

		await wrapper.get("button").trigger("click");
		await flushPromises();

		await vi.advanceTimersByTimeAsync(1000);
		expect(wrapper.text()).toContain("running");

		await vi.advanceTimersByTimeAsync(1000);
		expect(wrapper.text()).toContain("succeeded");

		// Terminal status: polling must stop.
		await vi.advanceTimersByTimeAsync(3000);
		expect(fetchMock).toHaveBeenCalledTimes(3);
	});

	it("shows an error when the creation fails", async () => {
		vi.stubGlobal(
			"fetch",
			vi.fn().mockRejectedValue(new Error("network error")),
		);
		const wrapper = mount(CreateAnalysis);

		await wrapper.get("button").trigger("click");
		await flushPromises();

		expect(wrapper.text()).toContain("Something went wrong");
	});
});
