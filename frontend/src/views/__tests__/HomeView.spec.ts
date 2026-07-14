import { flushPromises, mount } from "@vue/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import HomeView from "../HomeView.vue";

const { pushMock } = vi.hoisted(() => ({ pushMock: vi.fn() }));

vi.mock("vue-router", () => ({
	// Renders the target so tests can assert where a link points.
	RouterLink: {
		props: ["to"],
		template: '<a :data-to="JSON.stringify(to)"><slot /></a>',
	},
	useRouter: () => ({ push: pushMock }),
}));

interface AnalysisPayload {
	id: string;
	status: string;
	createdAt: string;
	sourceFilename: string;
	verbatimCount: number;
}

function analysis(status: string): AnalysisPayload {
	return {
		id: "0198b1c2-0000-7000-8000-000000000001",
		status,
		createdAt: "2026-07-13T00:00:00Z",
		sourceFilename: "feedback.csv",
		verbatimCount: 42,
	};
}

// Routes both the BackendStatus health check and the analyses list.
function stubFetch(
	analysesResponses: AnalysisPayload[][],
): ReturnType<typeof vi.fn> {
	let call = 0;
	const fetchMock = vi.fn((url: string) => {
		if (url === "/api/health") {
			return Promise.resolve({ ok: true } as Response);
		}
		const payload =
			analysesResponses[Math.min(call, analysesResponses.length - 1)];
		call += 1;
		return Promise.resolve({
			ok: true,
			json: () => Promise.resolve(payload),
		} as Response);
	});
	vi.stubGlobal("fetch", fetchMock);
	return fetchMock;
}

describe("HomeView", () => {
	beforeEach(() => {
		vi.useFakeTimers();
	});

	afterEach(() => {
		vi.useRealTimers();
		vi.unstubAllGlobals();
		pushMock.mockReset();
	});

	it("deletes the account after confirmation and returns to sign-in", async () => {
		const fetchMock = vi.fn((url: string, init?: RequestInit) => {
			if (url === "/api/health") {
				return Promise.resolve({ ok: true } as Response);
			}
			if (url === "/api/auth/account" && init?.method === "DELETE") {
				return Promise.resolve({ ok: true } as Response);
			}
			return Promise.resolve({
				ok: true,
				json: () => Promise.resolve([]),
			} as Response);
		});
		vi.stubGlobal("fetch", fetchMock);
		const wrapper = mount(HomeView);
		await flushPromises();

		// First click reveals the confirm step; nothing is deleted yet.
		await wrapper.get(".link-button").trigger("click");
		expect(fetchMock).not.toHaveBeenCalledWith("/api/auth/account", {
			method: "DELETE",
		});

		const confirm = wrapper
			.findAll("button")
			.find((button) => button.text() === "Confirm deletion");
		await confirm?.trigger("click");
		await flushPromises();

		expect(fetchMock).toHaveBeenCalledWith("/api/auth/account", {
			method: "DELETE",
		});
		expect(pushMock).toHaveBeenCalledWith({ name: "sign-in" });
	});

	it("shows an empty state and a link to create an analysis", async () => {
		stubFetch([[]]);
		const wrapper = mount(HomeView);
		await flushPromises();

		expect(wrapper.text()).toContain("No analyses yet");
		expect(wrapper.text()).toContain("New analysis");
	});

	it("lists the account's analyses with their status", async () => {
		stubFetch([[analysis("pending")]]);
		const wrapper = mount(HomeView);
		await flushPromises();

		expect(wrapper.text()).toContain("feedback.csv");
		expect(wrapper.text()).toContain("42 verbatims");
		expect(wrapper.get(".badge").attributes("data-status")).toBe("pending");
	});

	it("links each analysis to its detail screen", async () => {
		stubFetch([[analysis("succeeded")]]);
		const wrapper = mount(HomeView);
		await flushPromises();

		const link = wrapper.get(".analysis-list a");
		expect(JSON.parse(link.attributes("data-to") ?? "{}")).toEqual({
			name: "analysis-detail",
			params: { id: "0198b1c2-0000-7000-8000-000000000001" },
		});
	});

	it("polls until the analysis reaches a terminal status, then stops", async () => {
		const fetchMock = stubFetch([
			[analysis("pending")],
			[analysis("succeeded")],
		]);
		const wrapper = mount(HomeView);
		await flushPromises();
		expect(wrapper.get(".badge").attributes("data-status")).toBe("pending");

		await vi.advanceTimersByTimeAsync(1500);
		expect(wrapper.get(".badge").attributes("data-status")).toBe("succeeded");

		// Terminal: polling stops (no more /api/analyses calls).
		const callsAfterTerminal = fetchMock.mock.calls.filter(
			([url]) => url === "/api/analyses",
		).length;
		await vi.advanceTimersByTimeAsync(4500);
		const callsLater = fetchMock.mock.calls.filter(
			([url]) => url === "/api/analyses",
		).length;
		expect(callsLater).toBe(callsAfterTerminal);
	});
});
