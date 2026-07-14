import { flushPromises, mount } from "@vue/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import AnalysisDetailView from "../AnalysisDetailView.vue";

const ANALYSIS_ID = "0198b1c2-0000-7000-8000-000000000001";

vi.mock("vue-router", () => ({
	RouterLink: { template: "<a><slot /></a>" },
	useRoute: () => ({ params: { id: ANALYSIS_ID } }),
}));

interface RepresentativePayload {
	position: number;
	text: string;
}

interface ThemePayload {
	name: string;
	synthesis: string;
	verbatimCount: number;
	representatives: RepresentativePayload[];
}

interface DetailPayload {
	id: string;
	status: string;
	createdAt: string;
	sourceFilename: string;
	verbatimCount: number;
	processedCount: number;
	error: string | null;
	unclassifiedCount: number;
	themes: ThemePayload[];
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

	it("renders themes in the order received, with weight, synthesis and exact citations", async () => {
		stubFetch([
			detail({
				themes: [
					{
						name: "Performance",
						synthesis: "Speed disappoints.",
						verbatimCount: 8,
						representatives: [{ position: 1, text: "Too slow" }],
					},
					{
						name: "Praise",
						synthesis: "People are happy.",
						verbatimCount: 2,
						representatives: [],
					},
				],
			}),
		]);
		const wrapper = mount(AnalysisDetailView);
		await flushPromises();

		const themes = wrapper.findAll(".theme");
		expect(themes).toHaveLength(2);
		expect(themes[0].text()).toContain("Performance");
		expect(themes[0].text()).toContain("Speed disappoints.");
		expect(themes[0].text()).toContain("8 verbatims");
		// The citation is the original row, word for word, with its 1-based row.
		const quote = themes[0].get("blockquote");
		expect(quote.text()).toContain("Too slow");
		expect(quote.text()).toContain("row 2");
		// Weight bars are proportional to the largest theme.
		expect(themes[0].get(".weight-bar").attributes("style")).toContain(
			"width: 100%",
		);
		expect(themes[1].get(".weight-bar").attributes("style")).toContain(
			"width: 25%",
		);
		// A theme without representatives renders no quote — and no crash.
		expect(themes[1].findAll("blockquote")).toHaveLength(0);
		// Every verbatim classified: no loss notice.
		expect(wrapper.text()).not.toContain("could not be classified");
	});

	it("reports unclassified verbatims when there are any", async () => {
		stubFetch([
			detail({
				unclassifiedCount: 3,
				themes: [
					{
						name: "Performance",
						synthesis: "Speed disappoints.",
						verbatimCount: 7,
						representatives: [],
					},
				],
			}),
		]);
		const wrapper = mount(AnalysisDetailView);
		await flushPromises();

		expect(wrapper.text()).toContain(
			"3 verbatims could not be classified into any theme.",
		);
	});

	it("shows a dedicated message when the analysis succeeded without themes", async () => {
		stubFetch([detail({ themes: [] })]);
		const wrapper = mount(AnalysisDetailView);
		await flushPromises();

		expect(wrapper.text()).toContain("No themes were found in this corpus.");
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

	it("shows a not-found message on 404", async () => {
		vi.stubGlobal(
			"fetch",
			vi.fn(() => Promise.resolve({ ok: false, status: 404 } as Response)),
		);
		const wrapper = mount(AnalysisDetailView);
		await flushPromises();

		expect(wrapper.text()).toContain("This analysis was not found.");
	});

	it("escapes verbatim content instead of rendering it as HTML", async () => {
		// Sentinel against any future v-html: user content must stay text.
		const hostile = '<img src=x onerror="alert(1)">';
		stubFetch([
			detail({
				themes: [
					{
						name: hostile,
						synthesis: hostile,
						verbatimCount: 1,
						representatives: [{ position: 0, text: hostile }],
					},
				],
			}),
		]);
		const wrapper = mount(AnalysisDetailView);
		await flushPromises();

		expect(wrapper.find("img").exists()).toBe(false);
		expect(wrapper.html()).toContain("&lt;img");
		expect(wrapper.text()).toContain(hostile);
	});
});
