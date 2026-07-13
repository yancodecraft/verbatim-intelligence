<script setup lang="ts">
import { RouterView, useRouter } from "vue-router";
import { useSession } from "./session";

const session = useSession();
const router = useRouter();

async function signOut(): Promise<void> {
	await session.signOut();
	await router.push({ name: "sign-in" });
}
</script>

<template>
	<header>
		<h1>Verbatim Intelligence</h1>
		<p>Turn raw customer feedback into decisions — verbatim.</p>
		<p v-if="session.email.value" class="session">
			{{ session.email.value }}
			<button type="button" @click="signOut">Sign out</button>
		</p>
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
