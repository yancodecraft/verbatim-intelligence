<script setup lang="ts">
import { onMounted, onUnmounted, ref } from "vue";
import { RouterLink, useRoute } from "vue-router";
import AnalysisResults from "../components/AnalysisResults.vue";
import type { Theme } from "../types/analysis";

type AnalysisStatus = "pending" | "running" | "succeeded" | "failed";

interface AnalysisDetail {
	id: string;
	status: AnalysisStatus;
	createdAt: string;
	sourceFilename: string;
	verbatimCount: number;
	processedCount: number;
	error: string | null;
	unclassifiedCount: number;
	shared: boolean;
	themes: Theme[];
}

const POLL_INTERVAL_MS = 1500;

const route = useRoute();
const analysis = ref<AnalysisDetail | null>(null);
const loaded = ref(false);
const failed = ref(false);
const notFound = ref(false);
let pollTimer: ReturnType<typeof setInterval> | undefined;

async function load(): Promise<void> {
	try {
		const response = await fetch(`/api/analyses/${route.params.id}`);
		if (response.status === 404) {
			notFound.value = true;
			stopPolling();
			return;
		}
		if (!response.ok) {
			throw new Error(`unexpected status ${response.status}`);
		}
		analysis.value = await response.json();
		failed.value = false;
		// Keep polling only while the analysis is still being processed.
		if (settled()) {
			stopPolling();
		}
	} catch {
		failed.value = true;
		stopPolling();
	} finally {
		loaded.value = true;
	}
}

function settled(): boolean {
	const status = analysis.value?.status;
	return (
		notFound.value ||
		failed.value ||
		status === "succeeded" ||
		status === "failed"
	);
}

function stopPolling(): void {
	if (pollTimer !== undefined) {
		clearInterval(pollTimer);
		pollTimer = undefined;
	}
}

onMounted(async () => {
	await load();
	// The first load may already have settled things — don't start a timer
	// stopPolling() couldn't have cleared yet.
	if (!settled()) {
		pollTimer = setInterval(load, POLL_INTERVAL_MS);
	}
});

onUnmounted(stopPolling);
</script>

<template>
	<main>
		<RouterLink :to="{ name: 'home' }">Back to analyses</RouterLink>

		<p v-if="notFound">This analysis was not found.</p>
		<p v-else-if="failed">Something went wrong loading this analysis.</p>
		<p v-else-if="!loaded">Loading analysis…</p>
		<section v-else-if="analysis" class="analysis">
			<header class="analysis-header">
				<h2 class="filename">{{ analysis.sourceFilename }}</h2>
				<span class="badge" :data-status="analysis.status">{{ analysis.status }}</span>
			</header>

			<div
				v-if="analysis.status === 'pending' || analysis.status === 'running'"
				class="progress-panel"
			>
				<p>
					{{ analysis.processedCount }} / {{ analysis.verbatimCount }} verbatims
					processed
				</p>
				<progress
					:max="Math.max(analysis.verbatimCount, 1)"
					:value="analysis.processedCount"
				/>
			</div>

			<p v-else-if="analysis.status === 'failed'" class="error">
				{{ analysis.error }}
			</p>

			<AnalysisResults
				v-else
				:themes="analysis.themes"
				:unclassified-count="analysis.unclassifiedCount"
			/>
		</section>
	</main>
</template>

<style scoped>
.analysis-header {
	display: flex;
	align-items: baseline;
	justify-content: space-between;
	gap: 1rem;
	margin-top: 1rem;
}

.filename {
	overflow-wrap: anywhere;
}

.badge {
	text-transform: uppercase;
	font-size: 0.75rem;
	letter-spacing: 0.05em;
}

.progress-panel progress {
	width: 100%;
}

.error {
	color: #b00020;
	overflow-wrap: anywhere;
}
</style>
