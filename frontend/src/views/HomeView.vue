<script setup lang="ts">
import { onMounted, onUnmounted, ref } from "vue";
import { RouterLink, useRouter } from "vue-router";
import BackendStatus from "../components/BackendStatus.vue";
import { useSession } from "../session";

type AnalysisStatus = "pending" | "running" | "succeeded" | "failed";

interface Analysis {
	id: string;
	status: AnalysisStatus;
	createdAt: string;
	sourceFilename: string;
	verbatimCount: number;
}

const POLL_INTERVAL_MS = 1500;

const router = useRouter();
const session = useSession();

const analyses = ref<Analysis[]>([]);
const loaded = ref(false);
const failed = ref(false);
let pollTimer: ReturnType<typeof setInterval> | undefined;

const confirmingDeleteAccount = ref(false);
const deletingAccount = ref(false);
const deleteAccountFailed = ref(false);

async function deleteAccount(): Promise<void> {
	deletingAccount.value = true;
	deleteAccountFailed.value = false;
	try {
		const response = await fetch("/api/auth/account", { method: "DELETE" });
		if (!response.ok) {
			throw new Error(`unexpected status ${response.status}`);
		}
		session.clear();
		await router.push({ name: "sign-in" });
	} catch {
		deleteAccountFailed.value = true;
		deletingAccount.value = false;
	}
}

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

		<section class="account">
			<button
				v-if="!confirmingDeleteAccount"
				type="button"
				class="link-button"
				@click="confirmingDeleteAccount = true"
			>
				Delete my account
			</button>
			<template v-else>
				<span>
					Delete your account and all your analyses and uploads? This cannot be
					undone.
				</span>
				<button type="button" :disabled="deletingAccount" @click="deleteAccount">
					Confirm deletion
				</button>
				<button
					type="button"
					:disabled="deletingAccount"
					@click="confirmingDeleteAccount = false"
				>
					Cancel
				</button>
			</template>
			<p v-if="deleteAccountFailed" class="error">
				Something went wrong deleting your account.
			</p>
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

.account {
	display: flex;
	align-items: center;
	gap: 0.5rem;
	flex-wrap: wrap;
	margin-top: 3rem;
	font-size: 0.85rem;
}

.link-button {
	background: none;
	border: none;
	padding: 0;
	color: #b00020;
	text-decoration: underline;
	cursor: pointer;
	font-size: inherit;
}

.error {
	color: #b00020;
}
</style>
