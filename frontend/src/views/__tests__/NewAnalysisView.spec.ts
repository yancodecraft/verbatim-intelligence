import { flushPromises, mount } from "@vue/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import NewAnalysisView from "../NewAnalysisView.vue";

const pushMock = vi.fn();

vi.mock("vue-router", () => ({
	RouterLink: { template: "<a><slot /></a>" },
	useRouter: () => ({ push: pushMock }),
}));

const PREVIEW = {
	id: "0198b1c2-0000-7000-8000-000000000009",
	filename: "feedback.csv",
	columns: ["comment", "score"],
	sampleRows: [["Great product", "9"]],
	rowCount: 1,
};

function selectFile(wrapper: ReturnType<typeof mount>): Promise<void> {
	const input = wrapper.get('input[type="file"]');
	const file = new File(["comment,score\nGreat product,9\n"], "feedback.csv", {
		type: "text/csv",
	});
	Object.defineProperty(input.element, "files", { value: [file] });
	return input.trigger("change");
}

describe("NewAnalysisView", () => {
	beforeEach(() => {
		pushMock.mockReset();
	});

	afterEach(() => {
		vi.unstubAllGlobals();
	});

	it("uploads, previews columns, and runs the analysis on the chosen column", async () => {
		const fetchMock = vi
			.fn()
			.mockResolvedValueOnce({
				ok: true,
				json: () => Promise.resolve(PREVIEW),
			} as Response)
			.mockResolvedValueOnce({ ok: true } as Response);
		vi.stubGlobal("fetch", fetchMock);
		const wrapper = mount(NewAnalysisView);

		await selectFile(wrapper);
		await flushPromises();

		// The preview shows columns and a sample row (escaped by default).
		expect(wrapper.text()).toContain("comment");
		expect(wrapper.text()).toContain("Great product");
		expect(fetchMock.mock.calls[0]?.[0]).toBe("/api/uploads");

		await wrapper.get("button").trigger("click");
		await flushPromises();

		expect(fetchMock).toHaveBeenCalledWith("/api/analyses", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify({ uploadId: PREVIEW.id, verbatimColumn: "comment" }),
		});
		expect(pushMock).toHaveBeenCalledWith({ name: "home" });
	});

	it("shows the backend's rejection message as-is", async () => {
		vi.stubGlobal(
			"fetch",
			vi.fn().mockResolvedValue({
				ok: false,
				status: 400,
				json: () =>
					Promise.resolve({
						message: "The file is larger than the 5 MB limit.",
					}),
			} as Response),
		);
		const wrapper = mount(NewAnalysisView);

		await selectFile(wrapper);
		await flushPromises();

		expect(wrapper.text()).toContain("The file is larger than the 5 MB limit.");
		expect(wrapper.find("table").exists()).toBe(false);
	});
});
