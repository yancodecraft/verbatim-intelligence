import { flushPromises, mount } from "@vue/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Theme } from "../../types/analysis";
import AnalysisDetailView from "../AnalysisDetailView.vue";

const ANALYSIS_ID = "0198b1c2-0000-7000-8000-000000000001";

vi.mock("vue-router", () => ({
	RouterLink: { template: "<a><slot /></a>" },
	useRoute: () => ({ params: { id: ANALYSIS_ID } }),
}));

interface DetailPayload {
	id: string;
	status: string;
	createdAt: string;
	sourceFilename: string;
	verbatimCount: number;
	processedCount: number;
	error: string | null;
	unclassifiedCount: number;
	shared: boolean;
	themes: Theme[];
}

function detail(overrides: Partial<DetailPayload>): DetailPayload {
	return {
		id: ANALYSIS_ID,
		status: "succeeded",
		createdAt: "2026-07-13T00:00:00Z",
		sourceFilename: "feedback.csv",
		verbatimCount: 10,
		processedCount: 10,
		error: null,
		unclassifiedCount: 0,
		shared: false,
		themes: [],
		...overrides,
	};
}

function stubFetch(responses: DetailPayload[]): ReturnType<typeof vi.fn> {
	let call = 0;
	const fetchMock = vi.fn(() => {
		const payload = responses[Math.min(call, responses.length - 1)];
		call += 1;
		return Promise.resolve({
			ok: true,
			status: 200,
			json: () => Promise.resolve(payload),
		} as Response);
	});
	vi.stubGlobal("fetch", fetchMock);
	return fetchMock;
}

// Routes the detail poll and the share endpoints separately so the share
// interactions can be exercised on top of a loaded analysis.
function stubFetchWithShare(
	detailPayload: DetailPayload,
): ReturnType<typeof vi.fn> {
	const fetchMock = vi.fn((url: string, init?: RequestInit) => {
		if (url.endsWith("/share")) {
			if (init?.method === "POST") {
				return Promise.resolve({
					ok: true,
					status: 200,
					json: () =>
						Promise.resolve({ url: "https://app.example/shared/tok123" }),
				} as Response);
			}
			return Promise.resolve({ ok: true, status: 204 } as Response);
		}
		return Promise.resolve({
			ok: true,
			status: 200,
			json: () => Promise.resolve(detailPayload),
		} as Response);
	});
	vi.stubGlobal("fetch", fetchMock);
	return fetchMock;
}

describe("AnalysisDetailView", () => {
	beforeEach(() => {
		vi.useFakeTimers();
	});

	afterEach(() => {
		vi.useRealTimers();
		vi.unstubAllGlobals();
	});

	it("shows a loading state until the first response arrives", async () => {
		stubFetch([detail({})]);
		const wrapper = mount(AnalysisDetailView);

		expect(wrapper.text()).toContain("Loading analysis…");

		await flushPromises();
		expect(wrapper.text()).not.toContain("Loading analysis…");
	});

	it("shows progress while running, then polls until terminal and stops", async () => {
		const fetchMock = stubFetch([
			detail({ status: "running", processedCount: 3 }),
			detail({ status: "succeeded", processedCount: 10 }),
		]);
		const wrapper = mount(AnalysisDetailView);
		await flushPromises();

		expect(wrapper.text()).toContain("3 / 10 verbatims processed");
		expect(wrapper.get("progress").attributes("max")).toBe("10");
		expect(wrapper.get(".badge").attributes("data-status")).toBe("running");

		await vi.advanceTimersByTimeAsync(1500);
		expect(wrapper.get(".badge").attributes("data-status")).toBe("succeeded");

		// Terminal: polling stops (no more detail calls).
		const callsAfterTerminal = fetchMock.mock.calls.length;
		await vi.advanceTimersByTimeAsync(4500);
		expect(fetchMock.mock.calls.length).toBe(callsAfterTerminal);
	});

	it("renders the results once succeeded", async () => {
		stubFetch([
			detail({
				unclassifiedCount: 3,
				themes: [
					{
						name: "Performance",
						synthesis: "Speed disappoints.",
						verbatimCount: 8,
						representatives: [{ position: 1, text: "Too slow" }],
					},
				],
			}),
		]);
		const wrapper = mount(AnalysisDetailView);
		await flushPromises();

		// The rendering itself is covered by AnalysisResults.spec.ts; here we
		// only assert the view hands the payload over.
		expect(wrapper.get(".theme").text()).toContain("Performance");
		expect(wrapper.text()).toContain(
			"3 verbatims could not be classified into any theme.",
		);
	});

	it("shows the error of a failed analysis and does not poll again", async () => {
		const fetchMock = stubFetch([
			detail({
				status: "failed",
				error: "Analysis stopped at its cost cap.",
			}),
		]);
		const wrapper = mount(AnalysisDetailView);
		await flushPromises();

		expect(wrapper.text()).toContain("Analysis stopped at its cost cap.");
		expect(wrapper.get(".badge").attributes("data-status")).toBe("failed");

		const callsAfterTerminal = fetchMock.mock.calls.length;
		await vi.advanceTimersByTimeAsync(4500);
		expect(fetchMock.mock.calls.length).toBe(callsAfterTerminal);
	});

	it("creates a share link and shows the URL to copy", async () => {
		stubFetchWithShare(detail({}));
		const wrapper = mount(AnalysisDetailView);
		await flushPromises();

		await wrapper.get(".share-actions button").trigger("click");
		await flushPromises();

		const input = wrapper.find<HTMLInputElement>(".share-link input");
		expect(input.exists()).toBe(true);
		expect(input.element.value).toBe("https://app.example/shared/tok123");
		expect(wrapper.text()).toContain("Anyone with this link can read");
	});

	it("offers regenerate and revoke when already shared, and revokes", async () => {
		stubFetchWithShare(detail({ shared: true }));
		const wrapper = mount(AnalysisDetailView);
		await flushPromises();

		// After a reload the raw URL is gone — only regenerate/revoke remain.
		expect(wrapper.text()).toContain("This analysis is shared.");
		const buttons = wrapper.findAll(".share-actions button");
		expect(buttons.map((b) => b.text())).toEqual(["Regenerate link", "Revoke"]);

		const [, revoke] = buttons;
		if (!revoke) {
			throw new Error("expected a revoke button");
		}
		await revoke.trigger("click");
		await flushPromises();

		expect(wrapper.get(".share-actions button").text()).toBe(
			"Create share link",
		);
	});

	it("hides the share section while the analysis is not succeeded", async () => {
		stubFetch([detail({ status: "running", processedCount: 3 })]);
		const wrapper = mount(AnalysisDetailView);
		await flushPromises();

		expect(wrapper.find(".share").exists()).toBe(false);
	});

	it("shows a not-found message on 404", async () => {
		vi.stubGlobal(
			"fetch",
			vi.fn(() => Promise.resolve({ ok: false, status: 404 } as Response)),
		);
		const wrapper = mount(AnalysisDetailView);
		await flushPromises();

		expect(wrapper.text()).toContain("This analysis was not found.");
	});
});
