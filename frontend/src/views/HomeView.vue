<script setup lang="ts">
import { onMounted, onUnmounted, ref } from "vue";
import { RouterLink } from "vue-router";
import BackendStatus from "../components/BackendStatus.vue";

type AnalysisStatus = "pending" | "running" | "succeeded" | "failed";

interface Analysis {
	id: string;
	status: AnalysisStatus;
	createdAt: string;
	sourceFilename: string;
	verbatimCount: number;
}

const POLL_INTERVAL_MS = 1500;

const analyses = ref<Analysis[]>([]);
const loaded = ref(false);
const failed = ref(false);
let pollTimer: ReturnType<typeof setInterval> | undefined;

async function load(): Promise<void> {
	try {
		const response = await fetch("/api/analyses");
		if (!response.ok) {
			throw new Error(`unexpected status ${response.status}`);
		}
		analyses.value = await response.json();
		failed.value = false;
		// Keep polling only while something is still being processed.
		if (
			!analyses.value.some(
				(a) => a.status === "pending" || a.status === "running",
			)
		) {
			stopPolling();
		}
	} catch {
		failed.value = true;
		stopPolling();
	} finally {
		loaded.value = true;
	}
}

function stopPolling(): void {
	if (pollTimer !== undefined) {
		clearInterval(pollTimer);
		pollTimer = undefined;
	}
}

onMounted(async () => {
	await load();
	pollTimer = setInterval(load, POLL_INTERVAL_MS);
});

onUnmounted(stopPolling);
</script>

<template>
	<main>
		<BackendStatus />

		<section class="analyses">
			<div class="analyses-header">
				<h2>Analyses</h2>
				<RouterLink :to="{ name: 'new-analysis' }">New analysis</RouterLink>
			</div>

			<p v-if="failed">Something went wrong loading your analyses.</p>
			<p v-else-if="loaded && analyses.length === 0">
				No analyses yet. Upload a CSV of verbatims to get started.
			</p>
			<ul v-else class="analysis-list">
				<li v-for="analysis in analyses" :key="analysis.id">
					<RouterLink
						class="filename"
						:to="{ name: 'analysis-detail', params: { id: analysis.id } }"
					>
						{{ analysis.sourceFilename }}
					</RouterLink>
					<span class="count">{{ analysis.verbatimCount }} verbatims</span>
					<span class="badge" :data-status="analysis.status">{{ analysis.status }}</span>
				</li>
			</ul>
		</section>
	</main>
</template>

<style scoped>
.analyses-header {
	display: flex;
	align-items: baseline;
	justify-content: space-between;
	gap: 1rem;
}

.analysis-list {
	list-style: none;
	padding: 0;
}

.analysis-list li {
	display: flex;
	align-items: center;
	gap: 1rem;
	padding: 0.5rem 0;
	border-bottom: 1px solid rgba(128, 128, 128, 0.2);
}

.filename {
	flex: 1;
	font-weight: 600;
}

.count {
	opacity: 0.7;
}

.badge {
	text-transform: uppercase;
	font-size: 0.75rem;
	letter-spacing: 0.05em;
}
</style>
