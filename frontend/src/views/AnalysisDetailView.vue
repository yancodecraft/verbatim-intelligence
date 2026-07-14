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

// The raw share URL is only ever known right after creating it — the
// backend stores a hash. After a reload the screen only knows `shared`.
const shareUrl = ref<string | null>(null);
const shareBusy = ref(false);
const shareFailed = ref(false);
const copied = ref(false);

async function createShareLink(): Promise<void> {
	shareBusy.value = true;
	shareFailed.value = false;
	copied.value = false;
	try {
		const response = await fetch(`/api/analyses/${route.params.id}/share`, {
			method: "POST",
		});
		if (!response.ok) {
			throw new Error(`unexpected status ${response.status}`);
		}
		const body = await response.json();
		shareUrl.value = body.url;
		if (analysis.value) {
			analysis.value.shared = true;
		}
	} catch {
		shareFailed.value = true;
	} finally {
		shareBusy.value = false;
	}
}

async function revokeShareLink(): Promise<void> {
	shareBusy.value = true;
	shareFailed.value = false;
	try {
		const response = await fetch(`/api/analyses/${route.params.id}/share`, {
			method: "DELETE",
		});
		if (!response.ok) {
			throw new Error(`unexpected status ${response.status}`);
		}
		shareUrl.value = null;
		copied.value = false;
		if (analysis.value) {
			analysis.value.shared = false;
		}
	} catch {
		shareFailed.value = true;
	} finally {
		shareBusy.value = false;
	}
}

async function copyShareLink(): Promise<void> {
	if (shareUrl.value === null) {
		return;
	}
	await navigator.clipboard.writeText(shareUrl.value);
	copied.value = true;
}

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

			<template v-else>
				<section class="share">
					<template v-if="shareUrl !== null">
						<p>
							Anyone with this link can read the report — until you revoke it.
						</p>
						<div class="share-link">
							<input type="text" readonly :value="shareUrl" />
							<button type="button" :disabled="shareBusy" @click="copyShareLink">
								{{ copied ? "Copied" : "Copy" }}
							</button>
						</div>
					</template>
					<p v-else-if="analysis.shared">
						This analysis is shared. The link is only shown when created —
						regenerate to get a new one (the old link stops working).
					</p>
					<div class="share-actions">
						<button
							v-if="!analysis.shared"
							type="button"
							:disabled="shareBusy"
							@click="createShareLink"
						>
							Create share link
						</button>
						<template v-else>
							<button
								v-if="shareUrl === null"
								type="button"
								:disabled="shareBusy"
								@click="createShareLink"
							>
								Regenerate link
							</button>
							<button type="button" :disabled="shareBusy" @click="revokeShareLink">
								Revoke
							</button>
						</template>
					</div>
					<p v-if="shareFailed" class="error">
						Something went wrong updating the share link.
					</p>
				</section>

				<AnalysisResults
					:themes="analysis.themes"
					:unclassified-count="analysis.unclassifiedCount"
				/>
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

.share {
	margin: 1rem 0;
	padding: 0.75rem;
	background: var(--color-background-soft);
	border: 1px solid var(--color-border);
	border-radius: 4px;
}

.share-link {
	display: flex;
	gap: 0.5rem;
	margin: 0.5rem 0;
}

.share-link input {
	flex: 1;
	font-family: monospace;
	padding: 0.25rem 0.5rem;
}

.share-actions {
	display: flex;
	gap: 0.5rem;
}
</style>
