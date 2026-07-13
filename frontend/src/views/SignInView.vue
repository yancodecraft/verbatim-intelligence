<script setup lang="ts">
import { ref } from "vue";

const email = ref("");
const sent = ref(false);
const sending = ref(false);
const failed = ref(false);

async function requestLink(): Promise<void> {
	if (sending.value || email.value.trim() === "") {
		return;
	}
	sending.value = true;
	failed.value = false;
	try {
		const response = await fetch("/api/auth/magic-link", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: JSON.stringify({ email: email.value }),
		});
		if (!response.ok) {
			throw new Error(`unexpected status ${response.status}`);
		}
		sent.value = true;
	} catch {
		failed.value = true;
	} finally {
		sending.value = false;
	}
}
</script>

<template>
	<section class="sign-in">
		<h2>Sign in</h2>
		<p v-if="sent">
			Check your inbox — we sent a sign-in link to
			<strong>{{ email }}</strong>. It expires in 15 minutes.
		</p>
		<form v-else @submit.prevent="requestLink">
			<label for="email">E-mail address</label>
			<input
				id="email"
				v-model="email"
				type="email"
				name="email"
				autocomplete="email"
				required
			/>
			<button type="submit" :disabled="sending">Send me a sign-in link</button>
			<p v-if="failed">Something went wrong — please try again.</p>
		</form>
	</section>
</template>

<style scoped>
form {
	display: flex;
	flex-direction: column;
	gap: 0.5rem;
	max-width: 20rem;
}
</style>
