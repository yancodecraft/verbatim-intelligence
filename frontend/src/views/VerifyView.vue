<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { useSession } from "../session";

const route = useRoute();
const router = useRouter();
const session = useSession();

const failed = ref(false);

onMounted(async () => {
	const token = route.query.token;
	if (typeof token !== "string" || token === "") {
		failed.value = true;
		return;
	}
	try {
		const response = await fetch("/api/auth/verify", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify({ token }),
		});
		if (!response.ok) {
			throw new Error(`unexpected status ${response.status}`);
		}
		await session.check();
		await router.replace({ name: "home" });
	} catch {
		failed.value = true;
	}
});
</script>

<template>
	<section class="verify">
		<p v-if="failed">
			This sign-in link is invalid or has expired.
			<RouterLink :to="{ name: 'sign-in' }">Request a new one</RouterLink>
		</p>
		<p v-else>Signing you in…</p>
	</section>
</template>
