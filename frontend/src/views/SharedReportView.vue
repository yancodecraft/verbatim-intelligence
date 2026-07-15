<script setup lang="ts">
import { onMounted, onUnmounted, ref } from "vue";
import { useRoute } from "vue-router";
import AnalysisResults from "../components/AnalysisResults.vue";
import type { Theme } from "../types/analysis";

interface SharedReport {
	sourceFilename: string;
	createdAt: string;
	verbatimCount: number;
	unclassifiedCount: number;
	themes: Theme[];
}

const route = useRoute();
const report = ref<SharedReport | null>(null);
const loaded = ref(false);
const unavailable = ref(false);

// The page is public and its URL carries the token: keep crawlers out and
// never leak the URL through outgoing requests. The SPA shares one <head>
// with the private views, so the tags are removed when the page is left.
// The reverse proxy sets the equivalent headers; these tags also cover dev.
const metas: HTMLMetaElement[] = [];

function addMeta(name: string, content: string): void {
	const meta = document.createElement("meta");
	meta.name = name;
	meta.content = content;
	document.head.appendChild(meta);
	metas.push(meta);
}

async function load(): Promise<void> {
	try {
		// Encode the path segment: the router decodes it, so a crafted token
		// like "../analyses/<id>" would otherwise re-target another endpoint
		// (docs/security-review.md, B2).
		const token = encodeURIComponent(String(route.params.token));
		const response = await fetch(`/api/shared/${token}`);
		if (!response.ok) {
			// Revoked, unknown or broken: one generic message, no distinction.
			throw new Error(`unexpected status ${response.status}`);
		}
		report.value = await response.json();
	} catch {
		unavailable.value = true;
	} finally {
		loaded.value = true;
	}
}

onMounted(() => {
	addMeta("robots", "noindex");
	addMeta("referrer", "no-referrer");
	void load();
});

onUnmounted(() => {
	for (const meta of metas) {
		meta.remove();
	}
});
</script>

<template>
	<main>
		<p v-if="unavailable">This report is not available.</p>
		<p v-else-if="!loaded">Loading report…</p>
		<section v-else-if="report" class="report">
			<header class="report-header">
				<h2 class="filename">{{ report.sourceFilename }}</h2>
				<span class="count">{{ report.verbatimCount }} verbatims</span>
			</header>
			<AnalysisResults
				:themes="report.themes"
				:unclassified-count="report.unclassifiedCount"
			/>
		</section>
	</main>
</template>

<style scoped>
.report-header {
	display: flex;
	align-items: baseline;
	justify-content: space-between;
	gap: 1rem;
	margin-top: 1rem;
}

.filename {
	overflow-wrap: anywhere;
}

.count {
	opacity: 0.7;
	white-space: nowrap;
}
</style>
