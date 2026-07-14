<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from "vue";
import { RouterLink, useRoute } from "vue-router";

type AnalysisStatus = "pending" | "running" | "succeeded" | "failed";

interface Representative {
	position: number;
	text: string;
}

interface Theme {
	name: string;
	synthesis: string;
	verbatimCount: number;
	representatives: Representative[];
}

interface AnalysisDetail {
	id: string;
	status: AnalysisStatus;
	createdAt: string;
	sourceFilename: string;
	verbatimCount: number;
	processedCount: number;
	error: string | null;
	unclassifiedCount: number;
	themes: Theme[];
}

const POLL_INTERVAL_MS = 1500;

const route = useRoute();
const analysis = ref<AnalysisDetail | null>(null);
const loaded = ref(false);
const failed = ref(false);
const notFound = ref(false);
let pollTimer: ReturnType<typeof setInterval> | undefined;

// The worker orders themes by volume before assigning positions and the API
// returns them in position order: the received order IS the volume order.
// Weight bars are relative to the largest theme, not the corpus total — a
// verbatim may support several themes, so theme counts don't sum to it.
const maxThemeCount = computed(() =>
	Math.max(1, ...(analysis.value?.themes.map((t) => t.verbatimCount) ?? [])),
);

const unclassifiedNotice = computed(() => {
	const count = analysis.value?.unclassifiedCount ?? 0;
	const noun = count === 1 ? "verbatim" : "verbatims";
	return `${count} ${noun} could not be classified into any theme.`;
});

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

			<template v-else>
				<p v-if="analysis.themes.length === 0">
					No themes were found in this corpus.
				</p>
				<p v-if="analysis.unclassifiedCount > 0" class="unclassified">
					{{ unclassifiedNotice }}
				</p>
				<article
					v-for="(theme, index) in analysis.themes"
					:key="index"
					class="theme"
				>
					<header class="theme-header">
						<h3>{{ theme.name }}</h3>
						<span class="count">{{ theme.verbatimCount }} verbatims</span>
					</header>
					<div class="weight-track">
						<div
							class="weight-bar"
							:style="{ width: `${(theme.verbatimCount / maxThemeCount) * 100}%` }"
						/>
					</div>
					<p class="synthesis">{{ theme.synthesis }}</p>
					<blockquote
						v-for="representative in theme.representatives"
						:key="representative.position"
					>
						<p>{{ representative.text }}</p>
						<!-- Positions are 0-based source rows; people count from 1. -->
						<cite>row {{ representative.position + 1 }}</cite>
					</blockquote>
				</article>
			</template>
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

.unclassified {
	padding: 0.5rem 0.75rem;
	background: var(--color-background-mute);
	border: 1px solid var(--color-border);
	border-radius: 4px;
}

.theme {
	padding: 1rem 0;
	border-bottom: 1px solid rgba(128, 128, 128, 0.2);
}

.theme-header {
	display: flex;
	align-items: baseline;
	justify-content: space-between;
	gap: 1rem;
}

.theme-header h3 {
	overflow-wrap: anywhere;
}

.count {
	opacity: 0.7;
	white-space: nowrap;
}

.weight-track {
	height: 6px;
	margin: 0.5rem 0;
	background: var(--color-background-mute);
	border-radius: 3px;
	overflow: hidden;
}

.weight-bar {
	height: 100%;
	background: hsla(160, 100%, 37%, 1);
	border-radius: 3px;
}

.synthesis {
	overflow-wrap: anywhere;
}

.theme blockquote {
	margin: 0.75rem 0 0;
	padding: 0.25rem 0 0.25rem 0.75rem;
	border-left: 3px solid var(--color-border-hover);
}

.theme blockquote p {
	overflow-wrap: anywhere;
}

.theme cite {
	display: block;
	font-size: 0.8rem;
	opacity: 0.7;
}
</style>
