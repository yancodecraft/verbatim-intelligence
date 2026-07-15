<script setup lang="ts">
import { RouterLink, RouterView, useRouter } from "vue-router";
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

	<footer>
		<RouterLink :to="{ name: 'privacy' }">Privacy</RouterLink>
	</footer>
</template>

<style scoped>
header {
	margin-bottom: 2rem;
}

header p {
	opacity: 0.7;
}

footer {
	margin-top: 3rem;
	padding-top: 1rem;
	border-top: 1px solid rgba(128, 128, 128, 0.2);
	font-size: 0.85rem;
	opacity: 0.7;
}
</style>
