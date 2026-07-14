import { mount } from "@vue/test-utils";
import { describe, expect, it } from "vitest";
import type { Theme } from "../../types/analysis";
import AnalysisResults from "../AnalysisResults.vue";

function theme(overrides: Partial<Theme>): Theme {
	return {
		name: "Performance",
		synthesis: "Speed disappoints.",
		verbatimCount: 8,
		representatives: [],
		...overrides,
	};
}

describe("AnalysisResults", () => {
	it("renders themes in the order received, with weight, synthesis and exact citations", () => {
		const wrapper = mount(AnalysisResults, {
			props: {
				themes: [
					theme({
						representatives: [{ position: 1, text: "Too slow" }],
					}),
					theme({
						name: "Praise",
						synthesis: "People are happy.",
						verbatimCount: 2,
					}),
				],
				unclassifiedCount: 0,
			},
		});

		const themes = wrapper.findAll(".theme");
		expect(themes).toHaveLength(2);
		const [performance, praise] = themes;
		if (!performance || !praise) {
			throw new Error("expected two rendered themes");
		}
		expect(performance.text()).toContain("Performance");
		expect(performance.text()).toContain("Speed disappoints.");
		expect(performance.text()).toContain("8 verbatims");
		// The citation is the original row, word for word, with its 1-based row.
		const quote = performance.get("blockquote");
		expect(quote.text()).toContain("Too slow");
		expect(quote.text()).toContain("row 2");
		// Weight bars are proportional to the largest theme.
		expect(performance.get(".weight-bar").attributes("style")).toContain(
			"width: 100%",
		);
		expect(praise.get(".weight-bar").attributes("style")).toContain(
			"width: 25%",
		);
		// A theme without representatives renders no quote — and no crash.
		expect(praise.findAll("blockquote")).toHaveLength(0);
		// Every verbatim classified: no loss notice.
		expect(wrapper.text()).not.toContain("could not be classified");
	});

	it("reports unclassified verbatims when there are any", () => {
		const wrapper = mount(AnalysisResults, {
			props: {
				themes: [theme({ verbatimCount: 7 })],
				unclassifiedCount: 3,
			},
		});

		expect(wrapper.text()).toContain(
			"3 verbatims could not be classified into any theme.",
		);
	});

	it("shows a dedicated message when there are no themes", () => {
		const wrapper = mount(AnalysisResults, {
			props: { themes: [], unclassifiedCount: 0 },
		});

		expect(wrapper.text()).toContain("No themes were found in this corpus.");
	});

	it("escapes verbatim content instead of rendering it as HTML", () => {
		// Sentinel against any future v-html: user content must stay text.
		const hostile = '<img src=x onerror="alert(1)">';
		const wrapper = mount(AnalysisResults, {
			props: {
				themes: [
					theme({
						name: hostile,
						synthesis: hostile,
						verbatimCount: 1,
						representatives: [{ position: 0, text: hostile }],
					}),
				],
				unclassifiedCount: 0,
			},
		});

		expect(wrapper.find("img").exists()).toBe(false);
		expect(wrapper.html()).toContain("&lt;img");
		expect(wrapper.text()).toContain(hostile);
	});
});
