import { flushPromises, mount } from "@vue/test-utils";
import { afterEach, describe, expect, it, vi } from "vitest";
import SharedReportView from "../SharedReportView.vue";

const TOKEN = "a".repeat(43);

vi.mock("vue-router", () => ({
	useRoute: () => ({ params: { token: TOKEN } }),
}));

interface ReportPayload {
	sourceFilename: string;
	createdAt: string;
	verbatimCount: number;
	unclassifiedCount: number;
	themes: {
		name: string;
		synthesis: string;
		verbatimCount: number;
		representatives: { position: number; text: string }[];
	}[];
}

function report(overrides: Partial<ReportPayload>): ReportPayload {
	return {
		sourceFilename: "feedback.csv",
		createdAt: "2026-07-13T00:00:00Z",
		verbatimCount: 10,
		unclassifiedCount: 0,
		themes: [
			{
				name: "Performance",
				synthesis: "Speed disappoints.",
				verbatimCount: 8,
				representatives: [{ position: 1, text: "Too slow" }],
			},
		],
		...overrides,
	};
}

function stubFetch(payload: ReportPayload): ReturnType<typeof vi.fn> {
	const fetchMock = vi.fn(() =>
		Promise.resolve({
			ok: true,
			status: 200,
			json: () => Promise.resolve(payload),
		} as Response),
	);
	vi.stubGlobal("fetch", fetchMock);
	return fetchMock;
}

describe("SharedReportView", () => {
	afterEach(() => {
		vi.unstubAllGlobals();
		// Mounted views from previous tests are not unmounted: sweep their
		// metas so the removal assertion only sees this test's markup.
		for (const meta of document.head.querySelectorAll("meta")) {
			meta.remove();
		}
	});

	it("fetches the report by token and renders it", async () => {
		const fetchMock = stubFetch(report({ unclassifiedCount: 2 }));
		const wrapper = mount(SharedReportView);
		await flushPromises();

		expect(fetchMock).toHaveBeenCalledWith(`/api/shared/${TOKEN}`);
		expect(wrapper.text()).toContain("feedback.csv");
		expect(wrapper.text()).toContain("10 verbatims");
		const theme = wrapper.get(".theme");
		expect(theme.text()).toContain("Performance");
		expect(theme.get("blockquote").text()).toContain("Too slow");
		expect(theme.get("cite").text()).toBe("row 2");
		expect(wrapper.text()).toContain(
			"2 verbatims could not be classified into any theme.",
		);
	});

	it("shows one generic message when the link is revoked, unknown or broken", async () => {
		vi.stubGlobal(
			"fetch",
			vi.fn(() => Promise.resolve({ ok: false, status: 404 } as Response)),
		);
		const wrapper = mount(SharedReportView);
		await flushPromises();

		expect(wrapper.text()).toContain("This report is not available.");
	});

	it("adds noindex and no-referrer metas while mounted, removes them after", async () => {
		stubFetch(report({}));
		const wrapper = mount(SharedReportView);
		await flushPromises();

		expect(
			document.head
				.querySelector('meta[name="robots"]')
				?.getAttribute("content"),
		).toBe("noindex");
		expect(
			document.head
				.querySelector('meta[name="referrer"]')
				?.getAttribute("content"),
		).toBe("no-referrer");

		wrapper.unmount();
		expect(document.head.querySelector('meta[name="robots"]')).toBeNull();
		expect(document.head.querySelector('meta[name="referrer"]')).toBeNull();
	});

	it("escapes report content instead of rendering it as HTML", async () => {
		// The public page is the most exposed surface: user content stays text.
		const hostile = '<img src=x onerror="alert(1)">';
		stubFetch(
			report({
				sourceFilename: hostile,
				themes: [
					{
						name: hostile,
						synthesis: hostile,
						verbatimCount: 1,
						representatives: [{ position: 0, text: hostile }],
					},
				],
			}),
		);
		const wrapper = mount(SharedReportView);
		await flushPromises();

		expect(wrapper.find("img").exists()).toBe(false);
		expect(wrapper.html()).toContain("&lt;img");
	});
});
