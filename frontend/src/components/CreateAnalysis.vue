<script setup lang="ts">
import { onUnmounted, ref } from "vue";

type AnalysisStatus = "pending" | "running" | "succeeded" | "failed";

interface Analysis {
	id: string;
	status: AnalysisStatus;
	createdAt: string;
}

const POLL_INTERVAL_MS = 1000;

const analysis = ref<Analysis | null>(null);
const failed = ref(false);
const creating = ref(false);
let pollTimer: ReturnType<typeof setInterval> | undefined;

async function createAnalysis(): Promise<void> {
	// A single shared poll timer: concurrent creations would orphan the
	// previous interval, so clicks are ignored while one is in flight.
	if (creating.value) {
		return;
	}
	creating.value = true;
	stopPolling();
	failed.value = false;
	analysis.value = null;
	try {
		const response = await fetch("/api/analyses", { method: "POST" });
		if (!response.ok) {
			throw new Error(`unexpected status ${response.status}`);
		}
		analysis.value = await response.json();
		pollTimer = setInterval(refresh, POLL_INTERVAL_MS);
	} catch {
		failed.value = true;
	} finally {
		creating.value = false;
	}
}

async function refresh(): Promise<void> {
	if (!analysis.value) {
		return;
	}
	try {
		const response = await fetch(`/api/analyses/${analysis.value.id}`);
		if (!response.ok) {
			throw new Error(`unexpected status ${response.status}`);
		}
		analysis.value = await response.json();
		if (
			analysis.value?.status === "succeeded" ||
			analysis.value?.status === "failed"
		) {
			stopPolling();
		}
	} catch {
		failed.value = true;
		stopPolling();
	}
}

function stopPolling(): void {
	if (pollTimer !== undefined) {
		clearInterval(pollTimer);
		pollTimer = undefined;
	}
}

onUnmounted(stopPolling);
</script>

<template>
	<section class="create-analysis">
		<button type="button" :disabled="creating" @click="createAnalysis">
			Create analysis
		</button>
		<p v-if="failed">Something went wrong</p>
		<p v-else-if="analysis" :data-status="analysis.status">
			Analysis {{ analysis.id }} is {{ analysis.status }}
		</p>
	</section>
</template>
