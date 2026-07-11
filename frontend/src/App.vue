<script setup lang="ts">
import { RouterView } from "vue-router";
</script>

<template>
	<header>
		<h1>Verbatim Intelligence</h1>
		<p>Turn raw customer feedback into decisions — verbatim.</p>
	</header>

	<RouterView />
</template>

<style scoped>
header {
	margin-bottom: 2rem;
}

header p {
	opacity: 0.7;
}
</style>
