<script setup lang="ts">
import { ref } from "vue";
import { useRouter } from "vue-router";

interface UploadPreview {
	id: string;
	filename: string;
	columns: string[];
	sampleRows: string[][];
	rowCount: number;
}

const router = useRouter();

const preview = ref<UploadPreview | null>(null);
const selectedColumn = ref("");
const error = ref("");
const uploading = ref(false);
const running = ref(false);

async function messageFrom(response: Response): Promise<string> {
	try {
		const body = await response.json();
		return typeof body?.message === "string"
			? body.message
			: "The request was rejected.";
	} catch {
		return "The request was rejected.";
	}
}

async function onFileSelected(event: Event): Promise<void> {
	const input = event.target as HTMLInputElement;
	const file = input.files?.[0];
	if (!file || uploading.value) {
		return;
	}
	uploading.value = true;
	error.value = "";
	preview.value = null;
	selectedColumn.value = "";
	try {
		const form = new FormData();
		form.append("file", file);
		const response = await fetch("/api/uploads", {
			method: "POST",
			body: form,
		});
		if (!response.ok) {
			// The backend message is the user-facing message (shown as-is).
			error.value = await messageFrom(response);
			return;
		}
		preview.value = await response.json();
		selectedColumn.value = preview.value?.columns[0] ?? "";
	} catch {
		error.value = "Something went wrong while uploading the file.";
	} finally {
		uploading.value = false;
	}
}

async function runAnalysis(): Promise<void> {
	if (!preview.value || selectedColumn.value === "" || running.value) {
		return;
	}
	running.value = true;
	error.value = "";
	try {
		const response = await fetch("/api/analyses", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify({
				uploadId: preview.value.id,
				verbatimColumn: selectedColumn.value,
			}),
		});
		if (!response.ok) {
			error.value = await messageFrom(response);
			return;
		}
		await router.push({ name: "home" });
	} catch {
		error.value = "Something went wrong while starting the analysis.";
	} finally {
		running.value = false;
	}
}
</script>

<template>
	<main class="new-analysis">
		<h2>New analysis</h2>

		<p>
			<label for="csv-file">CSV file</label>
			<input
				id="csv-file"
				type="file"
				accept=".csv,text/csv"
				:disabled="uploading"
				@change="onFileSelected"
			/>
		</p>

		<p class="notice">
			Your verbatims are processed by a third-party AI service (Anthropic) to
			group them into themes. Only upload feedback you are allowed to share,
			and the raw file is discarded once the analysis is created.
		</p>

		<p v-if="error" class="error">{{ error }}</p>

		<section v-if="preview" class="preview">
			<p>
				<strong>{{ preview.filename }}</strong> — {{ preview.rowCount }} rows
			</p>

			<table>
				<thead>
					<tr>
						<th v-for="column in preview.columns" :key="column">{{ column }}</th>
					</tr>
				</thead>
				<tbody>
					<tr v-for="(row, index) in preview.sampleRows" :key="index">
						<td v-for="(cell, cellIndex) in row" :key="cellIndex">{{ cell }}</td>
					</tr>
				</tbody>
			</table>

			<fieldset>
				<legend>Which column holds the verbatim?</legend>
				<label v-for="column in preview.columns" :key="column" class="column-choice">
					<input
						type="radio"
						name="verbatim-column"
						:value="column"
						v-model="selectedColumn"
					/>
					{{ column }}
				</label>
			</fieldset>

			<button type="button" :disabled="running || selectedColumn === ''" @click="runAnalysis">
				Run analysis
			</button>
		</section>
	</main>
</template>

<style scoped>
.preview table {
	border-collapse: collapse;
	margin: 1rem 0;
}

.preview th,
.preview td {
	border: 1px solid rgba(128, 128, 128, 0.3);
	padding: 0.25rem 0.5rem;
	text-align: left;
}

fieldset {
	display: flex;
	flex-direction: column;
	gap: 0.25rem;
	margin: 1rem 0;
	border: none;
	padding: 0;
}

.column-choice {
	display: flex;
	gap: 0.5rem;
	align-items: center;
}

.error {
	color: #b00020;
}

.notice {
	font-size: 0.85rem;
	opacity: 0.7;
	max-width: 40rem;
}
</style>
