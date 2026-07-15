import { mount } from "@vue/test-utils";
import { describe, expect, it, vi } from "vitest";
import PrivacyView from "../PrivacyView.vue";

vi.mock("vue-router", () => ({
	RouterLink: { template: "<a><slot /></a>" },
}));

describe("PrivacyView", () => {
	it("states the operator, the sub-processors and the erasure right", () => {
		const wrapper = mount(PrivacyView);
		const text = wrapper.text();

		expect(text).toContain("YANTECH");
		expect(text).toContain("Anthropic");
		expect(text).toContain("Scaleway");
		expect(text).toContain("delete");
		expect(wrapper.get('a[href="mailto:privacy@yantech.fr"]')).toBeTruthy();
	});
});
