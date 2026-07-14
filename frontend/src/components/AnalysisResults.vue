<script setup lang="ts">
import { computed } from "vue";
import type { Theme } from "../types/analysis";

const props = defineProps<{
	themes: Theme[];
	unclassifiedCount: number;
}>();

// The worker orders themes by volume before assigning positions and the API
// returns them in position order: the received order IS the volume order.
// Weight bars are relative to the largest theme, not the corpus total — a
// verbatim may support several themes, so theme counts don't sum to it.
const maxThemeCount = computed(() =>
	Math.max(1, ...props.themes.map((t) => t.verbatimCount)),
);

const unclassifiedNotice = computed(() => {
	const noun = props.unclassifiedCount === 1 ? "verbatim" : "verbatims";
	return `${props.unclassifiedCount} ${noun} could not be classified into any theme.`;
});
</script>

<template>
	<p v-if="themes.length === 0">No themes were found in this corpus.</p>
	<p v-if="unclassifiedCount > 0" class="unclassified">
		{{ unclassifiedNotice }}
	</p>
	<article v-for="(theme, index) in themes" :key="index" class="theme">
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

<style scoped>
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
