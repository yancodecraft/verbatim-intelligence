<script setup lang="ts">
import { onMounted, ref } from "vue";

type Status = "checking" | "up" | "down";

const status = ref<Status>("checking");

onMounted(async () => {
	try {
		const response = await fetch("/api/health");
		status.value = response.ok ? "up" : "down";
	} catch {
		status.value = "down";
	}
});
</script>

<template>
	<p class="backend-status" :data-status="status">
		<template v-if="status === 'checking'">Checking backend…</template>
		<template v-else-if="status === 'up'">Backend is up</template>
		<template v-else>Backend is down</template>
	</p>
</template>
